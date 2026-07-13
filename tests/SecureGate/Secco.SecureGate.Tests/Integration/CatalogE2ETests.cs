extern alias logstream;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Infrastructure;
using Secco.SecureGate.Application;
using Secco.SecureGate.Client.Authorization;
using Secco.SecureGate.Client.Catalog;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O E2E da Fase 6.3: o LogStream sobe SEM NENHUM tenant em configuração — o
/// <c>SecureGateTenantCatalog</c> do pacote Client resolve connection strings direto do
/// SecureGate (client credentials com scope <c>catalog:logstream</c>), inclusive para as
/// migrations por tenant (<c>ListAsync</c>). Cadastrar tenant no SecureGate passa a ser
/// A operação de provisionamento da plataforma; desativar propaga em até um TTL.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class CatalogE2ETests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
    private const string AdminClientId = "e2e-admin-console";
    private const string AdminClientSecret = "e2e-admin-console-secret-32-chars!!!";
    private const string ServiceClientId = "e2e-logstream-service";
    private const string ServiceClientSecret = "e2e-logstream-service-secret-32ch!!!";
    private const string AppClientId = "e2e-app-writer";
    private const string AppClientSecret = "e2e-app-writer-secret-32-characters!";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private LogStreamHost _logStream = null!;
    private HttpClient _admin = null!;
    private Guid _tenantId;

    /// <summary>LogStream com Authority E catálogo de tenants apontando para o SecureGate de teste.</summary>
    private sealed class LogStreamHost(SelfIssuedAuthSecureGateApiFactory secureGate)
        : WebApplicationFactory<logstream::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Secco:Authentication:Audience"] = "secco-logstream",
                    ["Secco:Authentication:Authority"] = "http://localhost",
                    ["Secco:Authentication:RequireHttpsMetadata"] = "false",
                    // NENHUM Secco:Tenancy:Tenants — o catálogo remoto é a única fonte
                    ["Secco:SecureGate:BaseUrl"] = "http://localhost",
                    ["Secco:SecureGate:ClientId"] = ServiceClientId,
                    ["Secco:SecureGate:ClientSecret"] = ServiceClientSecret,
                    ["Secco:SecureGate:Product"] = "logstream",
                    // TTL mínimo: os testes verificam a propagação de desativação/revogação
                    ["Secco:SecureGate:CacheTtlSeconds"] = "1",
                    ["Secco:Authorization:CacheTtlSeconds"] = "1",
                }));

            builder.ConfigureServices(services =>
            {
                // Discovery/JWKS, catálogo e resolução de permissões trafegam pelo handler
                // in-memory do SecureGate
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    options.BackchannelHttpHandler = secureGate.Server.CreateHandler());

                services.AddHttpClient(SecureGateTenantCatalog.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => secureGate.Server.CreateHandler());

                services.AddHttpClient(SecureGatePermissionResolver.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => secureGate.Server.CreateHandler());
            });
        }
    }

    public async Task InitializeAsync()
    {
        await secureGate.EnsureDatabaseMigratedAsync();
        await secureGate.CreateClientAsync(AdminClientId, AdminClientSecret, SecureGateScopes.Admin);
        await secureGate.CreateClientAsync(ServiceClientId, ServiceClientSecret,
            "catalog:logstream", SecureGateScopes.AuthorizationRead);
        await secureGate.CreateClientWithRolesAsync(AppClientId, AppClientSecret, roles: AppRoleName, "logstream");

        // 1. O operador provisiona o tenant e o banco do LogStream via API de gestão
        _admin = await CreateAuthenticatedClientAsync(AdminClientId, AdminClientSecret, SecureGateScopes.Admin);

        var created = await _admin.PostAsJsonAsync("/api/v1/tenants",
            new { name = "Tenant E2E do catálogo", slug = $"t-{Guid.NewGuid():N}" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        _tenantId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var upsert = await _admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantId}/databases/logstream",
            new { connectionString = secureGate.GetConnectionStringFor("secco_logstream_catalog_e2e") });
        upsert.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 2. O role do client de aplicação nasce no tenant com as permissões do LogStream
        //    (Fase 6.4, ADR-0021) — tudo pela API de gestão
        (await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = AppRoleName }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await SetAppRolePermissionsAsync("log-entries:read", "log-entries:write");

        // 3. O LogStream sobe e aplica migrations iterando o catálogo REMOTO (ListAsync)
        _logStream = new LogStreamHost(secureGate);
        await _logStream.Services.MigrateLogStreamTenantDatabasesAsync();
    }

    private const string AppRoleName = "writer";

    private async Task SetAppRolePermissionsAsync(params string[] permissions) =>
        (await _admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles/{AppRoleName}/permissions",
            new { permissions }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

    public async Task DisposeAsync()
    {
        _admin?.Dispose();
        await _logStream.DisposeAsync();
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string clientId, string secret, string scope)
    {
        var client = secureGate.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await GetTokenAsync(clientId, secret, scope));

        return client;
    }

    private async Task<string> GetTokenAsync(string clientId, string secret, string scope)
    {
        var response = await secureGate.CreateClient().PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = secret,
                ["scope"] = scope,
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("access_token").GetString()!;
    }

    private async Task<HttpClient> CreateAppClientAsync()
    {
        var client = _logStream.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", await GetTokenAsync(AppClientId, AppClientSecret, "logstream"));
        client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, _tenantId.ToString());

        return client;
    }

    [Fact]
    public async Task LogStream_ResolvesTenantFromTheSecureGateCatalog()
    {
        var app = await CreateAppClientAsync();

        // Ingestão: o worker resolve a connection string do tenant NO SECUREGATE
        var ingest = await app.PostAsJsonAsync("/api/v1/log-entries", new
        {
            level = "Information",
            message = "primeiro log com tenant resolvido pelo catálogo central",
        });

        ingest.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await ingest.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        // Consulta: o caminho de request também resolve via catálogo remoto (FindAsync + cache)
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpResponseMessage persisted;

        do
        {
            persisted = await app.GetAsync($"/api/v1/log-entries/{id}");

            if (persisted.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        persisted.StatusCode.Should().Be(HttpStatusCode.OK,
            "zero tenants em configuração — a connection string veio do SecureGate");
    }

    [Fact]
    public async Task DeactivatedTenant_StopsResolvingWithinOneTtl()
    {
        var app = await CreateAppClientAsync();

        // Com o tenant ativo, a consulta resolve (404 do registro inexistente, não do tenant)
        (await app.GetAsync($"/api/v1/log-entries/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _admin.PostAsync($"/api/v1/tenants/{_tenantId}/deactivate", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Expira o TTL de 1s do cache do catálogo no LogStream
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var afterDeactivation = await app.GetAsync($"/api/v1/log-entries/{Guid.NewGuid()}");

        afterDeactivation.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "tenant desativado some do catálogo e o pipeline de tenancy responde 400 (tenant desconhecido)");

        // Reativa: o tenant volta a resolver no próximo refresh (e não suja os demais testes)
        (await _admin.PostAsync($"/api/v1/tenants/{_tenantId}/activate", content: null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task RevokedPermission_StopsAuthorizingWithinOneTtl_WithoutWaitingTokenExpiry()
    {
        var app = await CreateAppClientAsync();

        // Com log-entries:write concedida ao role, a ingestão autoriza
        (await app.PostAsJsonAsync("/api/v1/log-entries",
            new { level = "Information", message = "antes da revogação" }))
            .StatusCode.Should().Be(HttpStatusCode.Accepted);

        // O operador revoga a escrita (PUT idempotente: o conjunto novo não a contém)
        await SetAppRolePermissionsAsync("log-entries:read");

        // Expira o TTL de 1s do cache de permissões do LogStream
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        (await app.PostAsJsonAsync("/api/v1/log-entries",
            new { level = "Information", message = "depois da revogação" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden,
                "a razão de ser da ADR-0021: revogação propaga em ≤ 1 TTL com o MESMO token ainda válido");

        // A leitura segue autorizada — a revogação é granular por permissão
        (await app.GetAsync($"/api/v1/log-entries/{Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
