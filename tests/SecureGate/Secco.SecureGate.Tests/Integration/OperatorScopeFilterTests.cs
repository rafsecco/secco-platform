using System.Net;
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
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Filtro do scope admin no login (Fase 7.1, ADR-0023, defesa em profundidade da ADR-0020):
/// o scope <c>securegate:admin</c> só é emitido a usuários com o role <c>platform-operator</c>
/// — mesmo que o client tenha o scope permitido, login de usuário comum NÃO escala para admin.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public partial class OperatorScopeFilterTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
    private const string ClientId = "scope-filter-e2e";
    private const string RedirectUri = "https://localhost/callback";
    private const string Password = "Sc0pe@Filter!";
    private const string RequestedScope = "openid securegate:admin logstream";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _operatorEmail = $"op-{Guid.NewGuid():N}@secco.test";
    private readonly string _regularEmail = $"reg-{Guid.NewGuid():N}@secco.test";

    [GeneratedRegex("__RequestVerificationToken.*?value=\"([^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex AntiforgeryField();

    public async Task InitializeAsync()
    {
        await secureGate.EnsureDatabaseMigratedAsync();
        await secureGate.CreatePublicClientAsync(ClientId, RedirectUri, "securegate:admin", "logstream");

        using var scope = secureGate.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        // Operador: no tenant de plataforma, com o role platform-operator (semeado por referência)
        var normalizedOperator = SecureGatePlatform.OperatorRole.ToUpperInvariant();
        var operatorRole = await context.Roles.FirstAsync(
            r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == normalizedOperator);

        var operatorUser = new User
        {
            Id = Guid.CreateVersion7(),
            TenantId = SecureGatePlatform.TenantId,
            UserName = _operatorEmail,
            Email = _operatorEmail,
            EmailConfirmed = true,
        };
        (await userManager.CreateAsync(operatorUser, Password)).Succeeded.Should().BeTrue();
        context.UserRoles.Add(new UserRole { UserId = operatorUser.Id, RoleId = operatorRole.Id });

        // Usuário comum: num tenant qualquer, sem o role de operador
        var tenant = new Tenant("Tenant comum", $"t-{Guid.NewGuid():N}");
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();

        var regularUser = new User
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            UserName = _regularEmail,
            Email = _regularEmail,
            EmailConfirmed = true,
        };
        (await userManager.CreateAsync(regularUser, Password)).Succeeded.Should().BeTrue();
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OperatorLogin_ReceivesAdminScope()
    {
        var token = await LoginAndReadAccessTokenAsync(_operatorEmail);

        Scopes(token).Should().Contain(SecureGateScopes.Admin, "o operador de plataforma recebe o scope admin");
        Scopes(token).Should().Contain("logstream");
    }

    [Fact]
    public async Task RegularUserLogin_DoesNotReceiveAdminScope()
    {
        var token = await LoginAndReadAccessTokenAsync(_regularEmail);

        Scopes(token).Should().NotContain(SecureGateScopes.Admin,
            "usuário comum não escala para admin, mesmo com o client permitindo o scope (ADR-0023)");
        Scopes(token).Should().Contain("logstream", "os demais scopes seguem sendo concedidos normalmente");
    }

    [Fact]
    public async Task OperatorLogin_TokenHasNoTenantClaim()
    {
        var token = await LoginAndReadAccessTokenAsync(_operatorEmail);

        token.Claims.Should().NotContain(c => c.Type == "tenant_id",
            "o operador é tenant-less para dados — escolhe o tenant por requisição via X-Tenant-Id (ADR-0024)");
    }

    [Fact]
    public async Task RegularUserLogin_TokenCarriesTenantClaim()
    {
        var token = await LoginAndReadAccessTokenAsync(_regularEmail);

        token.Claims.Should().Contain(c => c.Type == "tenant_id",
            "usuário comum segue com tenant_id no token (isolamento da ADR-0005 intacto)");
    }

    // O JWT carrega um único claim 'scope' space-delimited (RFC padrão)
    private static IReadOnlyList<string> Scopes(JsonWebToken token) =>
        [.. token.Claims.Where(c => c.Type == "scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))];

    private async Task<JsonWebToken> LoginAndReadAccessTokenAsync(string email)
    {
        var browser = secureGate.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var authorizeUrl = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = RedirectUri,
            ["scope"] = RequestedScope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = Guid.NewGuid().ToString("N"),
        });

        var loginUrl = (await browser.GetAsync(authorizeUrl)).Headers.Location!.ToString();
        var loginPage = await browser.GetAsync(loginUrl);
        var antiforgery = AntiforgeryField().Match(await loginPage.Content.ReadAsStringAsync()).Groups[1].Value;

        var loginPost = await browser.PostAsync(loginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = Password,
            ["__RequestVerificationToken"] = antiforgery,
        }));

        var codeResponse = await browser.GetAsync(loginPost.Headers.Location!.ToString());
        var code = QueryHelpers.ParseQuery(codeResponse.Headers.Location!.Query)["code"].ToString();

        var tokenResponse = await browser.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = RedirectUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = verifier,
        }));
        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var accessToken = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("access_token").GetString();

        return new JsonWebTokenHandler().ReadJsonWebToken(accessToken);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
