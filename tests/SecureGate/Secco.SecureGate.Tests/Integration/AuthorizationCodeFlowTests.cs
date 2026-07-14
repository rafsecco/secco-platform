using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O E2E do login de usuário (Fase 6.5): authorization code + PKCE ponta a ponta contra o
/// SecureGate validando os PRÓPRIOS tokens (JWKS). Dirige o fluxo real do navegador —
/// desafio → tela de login (antiforgery) → code no redirect → troca por token com o
/// code_verifier → userinfo → refresh. As claims curtas da ADR-0007 saem no access token.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public partial class AuthorizationCodeFlowTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
    private const string ClientId = "webapp-e2e";
    private const string RedirectUri = "https://localhost/callback";
    private const string RoleName = "leitor";
    private const string Password = "L0gin@Secco!";
    private const string Scope = "openid profile email roles offline_access logstream";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _email = $"login-{Guid.NewGuid():N}@secco.test";
    private Guid _userId;
    private Guid _tenantId;

    [GeneratedRegex("__RequestVerificationToken.*?value=\"([^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex AntiforgeryField();

    public async Task InitializeAsync()
    {
        await secureGate.EnsureDatabaseMigratedAsync();
        await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");

        using var scope = secureGate.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var tenant = new Tenant("Tenant do login E2E", $"t-{Guid.NewGuid():N}");
        context.Tenants.Add(tenant);

        var role = new Role
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Name = RoleName,
            NormalizedName = RoleName.ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
        context.Roles.Add(role);
        context.RoleClaims.Add(new RoleClaim
        {
            RoleId = role.Id,
            ClaimType = "permission", // RoleRepository.PermissionClaimType (interno) — permissão não é asserida aqui
            ClaimValue = "log-entries:read",
        });
        await context.SaveChangesAsync();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            UserName = _email,
            Email = _email,
            EmailConfirmed = true,
        };
        (await userManager.CreateAsync(user, Password)).Succeeded.Should().BeTrue();

        context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await context.SaveChangesAsync();

        _userId = user.Id;
        _tenantId = tenant.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient CreateBrowser() => secureGate.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private static (string Verifier, string Challenge) CreatePkce()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        return (verifier, challenge);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Executa o fluxo até obter o authorization code (desafio → login → code no redirect).</summary>
    private async Task<string> ObtainAuthorizationCodeAsync(HttpClient browser, string challenge, string state)
    {
        var authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["nonce"] = Guid.NewGuid().ToString("N"),
        });

        // 1. Sem cookie → 302 para a tela de login
        var challengeResponse = await browser.GetAsync(authorizeUrl);
        challengeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        // O cookie handler redireciona com Location absoluto (http://localhost/login?ReturnUrl=...)
        var loginUrl = challengeResponse.Headers.Location!.ToString();
        loginUrl.Should().Contain("/login").And.Contain("ReturnUrl");

        // 2. Renderiza a tela e captura o token antiforgery (o cookie fica no CookieContainer)
        var loginPage = await browser.GetAsync(loginUrl);
        loginPage.EnsureSuccessStatusCode();
        var antiforgery = AntiforgeryField().Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;
        antiforgery.Should().NotBeNullOrEmpty();

        // 3. Submete as credenciais → 302 de volta à requisição de autorização
        var loginPost = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = _email,
            ["Input.Password"] = Password,
            ["__RequestVerificationToken"] = antiforgery,
        }));
        loginPost.StatusCode.Should().Be(HttpStatusCode.Redirect);

        // 4. Autenticado, o authorize emite o code e redireciona ao redirect_uri
        var codeResponse = await browser.GetAsync(loginPost.Headers.Location!.ToString());
        codeResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var callback = codeResponse.Headers.Location!;
        callback.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);

        var query = QueryHelpers.ParseQuery(callback.Query);
        query["state"].ToString().Should().Be(state, "o state protege contra CSRF no callback");

        return query["code"].ToString();
    }

    [Fact]
    public async Task AuthorizationCodeWithPkce_IssuesTokensWithShortClaims()
    {
        var browser = CreateBrowser();
        var (verifier, challenge) = CreatePkce();
        var state = Guid.NewGuid().ToString("N");

        var code = await ObtainAuthorizationCodeAsync(browser, challenge, state);

        var tokenResponse = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        }));

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Json);

        payload.TryGetProperty("id_token", out _).Should().BeTrue("openid foi solicitado");
        payload.TryGetProperty("refresh_token", out _).Should().BeTrue("offline_access foi solicitado");

        var accessToken = new JsonWebTokenHandler().ReadJsonWebToken(payload.GetProperty("access_token").GetString());
        accessToken.GetClaim("sub").Value.Should().Be(_userId.ToString());
        accessToken.GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString(), "o access token carrega o tenant (ADR-0005)");
        accessToken.Claims.Where(c => c.Type == "role").Select(c => c.Value)
            .Should().Contain(RoleName, "a claim curta role alimenta a autorização da ADR-0021");
        accessToken.Audiences.Should().Contain("secco-logstream", "o scope logstream mapeia para a audience do produto");
    }

    [Fact]
    public async Task AuthorizationCode_ThenUserInfo_ReturnsUserClaims()
    {
        var browser = CreateBrowser();
        var (verifier, challenge) = CreatePkce();
        var code = await ObtainAuthorizationCodeAsync(browser, challenge, Guid.NewGuid().ToString("N"));

        var tokenResponse = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        }));
        var accessToken = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("access_token").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var userInfo = await browser.SendAsync(request);

        userInfo.StatusCode.Should().Be(HttpStatusCode.OK);
        var info = await userInfo.Content.ReadFromJsonAsync<JsonElement>(Json);
        info.GetProperty("sub").GetString().Should().Be(_userId.ToString());
        info.GetProperty("email").GetString().Should().Be(_email);
        info.GetProperty("tenant_id").GetString().Should().Be(_tenantId.ToString());
    }

    [Fact]
    public async Task RefreshToken_IssuesNewAccessToken()
    {
        var browser = CreateBrowser();
        var (verifier, challenge) = CreatePkce();
        var code = await ObtainAuthorizationCodeAsync(browser, challenge, Guid.NewGuid().ToString("N"));

        var first = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        }));
        var refreshToken = (await first.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("refresh_token").GetString();

        var refreshed = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken!,
            ["client_id"] = ClientId,
        }));

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var accessToken = new JsonWebTokenHandler().ReadJsonWebToken(
            (await refreshed.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString());
        accessToken.GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString());
    }

    [Fact]
    public async Task TokenExchange_WithWrongCodeVerifier_IsRejected()
    {
        var browser = CreateBrowser();
        var (_, challenge) = CreatePkce();
        var code = await ObtainAuthorizationCodeAsync(browser, challenge, Guid.NewGuid().ToString("N"));

        var tokenResponse = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = Base64Url(RandomNumberGenerator.GetBytes(32)),
        }));

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest, "PKCE: o code_verifier tem de casar com o challenge");
    }

    [Fact]
    public async Task Login_WithWrongPassword_StaysOnTheFormWithoutCode()
    {
        var browser = CreateBrowser();
        var (_, challenge) = CreatePkce();

        var authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = Guid.NewGuid().ToString("N"),
        });

        var loginUrl = (await browser.GetAsync(authorizeUrl)).Headers.Location!.ToString();
        var loginPage = await browser.GetAsync(loginUrl);
        var antiforgery = AntiforgeryField().Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;

        var loginPost = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = _email,
            ["Input.Password"] = "senha-errada",
            ["__RequestVerificationToken"] = antiforgery,
        }));

        // Sem redirect: a página é re-renderizada com o erro (200), sem emitir code
        loginPost.StatusCode.Should().Be(HttpStatusCode.OK);
        // O Razor codifica acentos em entidades HTML — asserta no container de erro (ASCII)
        (await loginPost.Content.ReadAsStringAsync()).Should().Contain("role=\"alert\"");
    }
}
