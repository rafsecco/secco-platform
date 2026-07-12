using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Authentication;

public class SeccoAuthenticationTests : IAsyncLifetime
{
    private const string SigningKey = "chave-de-testes-com-32-caracteres!!";
    private const string Issuer = "secco-tests";
    private const string Audience = "secco-tests";

    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.UseEnvironment(Environments.Development);
                webBuilder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Secco:Authentication:Audience"] = Audience,
                        ["Secco:Authentication:Issuer"] = Issuer,
                        ["Secco:Authentication:DevelopmentSigningKey"] = SigningKey,
                    }));
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSeccoPlatform();
                });
                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseSeccoPlatform();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapSeccoPlatform();

                        // Sem metadata: protegido pela FallbackPolicy (fail-closed, ADR-0020)
                        endpoints.MapGet("/protegido", (ClaimsPrincipal user, ITenantContext tenant) =>
                            $"{user.Identity!.Name}|{user.IsInRole("Admin")}|{(tenant.IsResolved ? tenant.TenantId.ToString() : "sem-tenant")}");
                    });
                });
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    private static string CreateToken(
        string audience = Audience,
        Guid? tenantId = null,
        string? role = null)
    {
        var claims = new Dictionary<string, object> { [SeccoClaims.Subject] = "user-1" };

        if (tenantId is not null)
        {
            claims[SeccoClaims.TenantId] = tenantId.Value.ToString();
        }

        if (role is not null)
        {
            claims[SeccoClaims.Role] = role;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Claims = claims,
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }

    private HttpClient CreateClientWithToken(string? token = null)
    {
        var client = _host.GetTestClient();

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var response = await CreateClientWithToken().GetAsync("/protegido");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a FallbackPolicy protege endpoints sem metadata explícita (fail-closed)");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithValidToken_ReturnsShortClaims()
    {
        var response = await CreateClientWithToken(CreateToken(role: "Admin")).GetAsync("/protegido");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().StartWith("user-1|True|",
            "NameClaimType=sub e RoleClaimType=role sem remapeamento (ADR-0007)");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTenantClaim_ResolvesTenantAfterAuthentication()
    {
        var tenantId = Guid.NewGuid();

        var response = await CreateClientWithToken(CreateToken(tenantId: tenantId)).GetAsync("/protegido");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().EndWith($"|{tenantId}",
            "a ordem do UseSeccoPlatform garante autenticação antes da tenancy");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongAudience_ReturnsUnauthorized()
    {
        var response = await CreateClientWithToken(CreateToken(audience: "outra-api")).GetAsync("/protegido");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HealthEndpoints_WithoutToken_RemainAnonymous()
    {
        var client = CreateClientWithToken();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
