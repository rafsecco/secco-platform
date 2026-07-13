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
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O teste da plataforma: o LogStream valida um token EMITIDO PELO SECUREGATE via
/// JWKS/discovery (ADR-0007/0022) — dois produtos reais, Authority real, sem chave
/// simétrica. O tenant alvo viaja no header X-Tenant-Id (serviço-a-serviço sem claim
/// de tenant — o cenário interno da ADR-0005).
/// </summary>
public class CrossProductTokenFlowTests(SecureGateApiFactory secureGate) : IClassFixture<SecureGateApiFactory>, IAsyncLifetime
{
    private const string ClientId = "logstream-writer";
    private const string ClientSecret = "logstream-writer-secret-32-chars-min!";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private LogStreamHost _logStream = null!;

    /// <summary>LogStream com Authority apontando para o SecureGate de teste (backchannel in-memory).</summary>
    private sealed class LogStreamHost(SecureGateApiFactory secureGate, Guid tenantId)
        : WebApplicationFactory<logstream::Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    // Authority REAL (o SecureGate) em vez da chave HS256 de desenvolvimento
                    ["Secco:Authentication:Audience"] = "secco-logstream",
                    ["Secco:Authentication:Authority"] = "http://localhost",
                    ["Secco:Authentication:RequireHttpsMetadata"] = "false",
                    [$"Secco:Tenancy:Tenants:{tenantId}:ConnectionString"] =
                        secureGate.GetConnectionStringFor("secco_logstream_e2e"),
                }));

            builder.ConfigureServices(services =>
                // O discovery/JWKS trafega pelo handler in-memory do TestServer do SecureGate.
                // Configure (não PostConfigure): o post-configure interno do JwtBearer cria o
                // ConfigurationManager e precisa encontrar o handler já definido.
                services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    options.BackchannelHttpHandler = secureGate.Server.CreateHandler()));
        }
    }

    public async Task InitializeAsync()
    {
        await secureGate.EnsureDatabaseMigratedAsync();
        await secureGate.CreateClientAsync(ClientId, ClientSecret, "logstream");

        _logStream = new LogStreamHost(secureGate, TenantId);
        await _logStream.Services.MigrateLogStreamTenantDatabasesAsync();
    }

    public async Task DisposeAsync() => await _logStream.DisposeAsync();

    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task LogStream_Always_AcceptsTokenIssuedBytheSecureGate()
    {
        // 1. SecureGate emite o token (client credentials, scope logstream)
        var tokenResponse = await secureGate.CreateClient().PostAsync("/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = ClientId,
                ["client_secret"] = ClientSecret,
                ["scope"] = "logstream",
            }));

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var accessToken = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("access_token").GetString()!;

        // 2. LogStream valida o token via JWKS do SecureGate e aceita a ingestão
        var logStreamClient = _logStream.CreateClient();
        logStreamClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        logStreamClient.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, TenantId.ToString());

        var ingest = await logStreamClient.PostAsJsonAsync("/api/v1/log-entries", new
        {
            level = "Information",
            message = "primeiro log com token real da plataforma",
        });

        ingest.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "o LogStream valida contra a Authority real — sem chave simétrica compartilhada");

        var id = (await ingest.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        // 3. A consulta com o mesmo token encontra o registro persistido
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpResponseMessage persisted;

        do
        {
            persisted = await logStreamClient.GetAsync($"/api/v1/log-entries/{id}");

            if (persisted.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        persisted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LogStream_WithForgedToken_Returns401()
    {
        var client = _logStream.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "token-forjado");
        client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, TenantId.ToString());

        var response = await client.PostAsJsonAsync("/api/v1/log-entries", new
        {
            level = "Information",
            message = "não deve entrar",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
