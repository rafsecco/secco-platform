using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Secco.LogStream.Infrastructure;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.LogStream.Tests.Integration.Helpers;
using Testcontainers.PostgreSql;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

/// <summary>
/// Paridade do segundo provider (ADR-0018): a API inteira sobe sobre PostgreSQL —
/// migrations do assembly próprio aplicam do zero, o schema é idêntico (minúsculo,
/// sem aspas — ADR-0017) e o fluxo de ingestão E2E funciona.
/// A suite completa segue no provider padrão (SQL Server).
/// </summary>
public sealed class LogStreamPostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder().Build();

    public Guid TenantAlfa { get; } = Guid.NewGuid();

    public string GetTenantConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["LogStream:Database:Provider"] = "PostgreSql",
                ["Secco:Authentication:Audience"] = "secco-logstream",
                ["Secco:Authentication:Issuer"] = "secco-tests",
                ["Secco:Authentication:DevelopmentSigningKey"] = "chave-de-testes-com-32-caracteres!!",
                [$"Secco:Tenancy:Tenants:{TenantAlfa}:ConnectionString"] =
                    GetTenantConnectionString("secco_logstream_pg_alfa"),
                // Permissões do role dos tokens de teste (Fase 6.4, ADR-0021)
                ["Secco:Authorization:Roles:test-admin:Permissions:0"] = "log-entries:read",
                ["Secco:Authorization:Roles:test-admin:Permissions:1"] = "log-entries:write",
            }));
    }

    public async Task InitializeAsync() => await _container.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}

public class PostgresParityTests(LogStreamPostgresApiFactory factory) : IClassFixture<LogStreamPostgresApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private LogStreamDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<LogStreamDbContext>()
            .UseNpgsql(factory.GetTenantConnectionString("secco_logstream_pg_alfa"))
            .Options);

    [Fact]
    public async Task Migrations_OnPostgres_ApplyFromScratchWithUnquotedLowercaseSchema()
    {
        await factory.Services.MigrateLogStreamTenantDatabasesAsync();

        await using var context = CreateContext();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

        // Identificadores sem aspas resolvem — a promessa da nomenclatura minúscula (ADR-0017)
        var act = () => context.Database.ExecuteSqlRawAsync(
            "SELECT id_pk_log_entry, ds_message, ie_level FROM tb_log_entries WHERE 1 = 0");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task IngestAndQuery_OnPostgres_WorksEndToEnd()
    {
        await factory.Services.MigrateLogStreamTenantDatabasesAsync();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", JwtTestTokenFactory.CreateToken(factory.TenantAlfa));

        var response = await client.PostAsJsonAsync("/api/v1/log-entries", new
        {
            level = "Warning",
            message = "paridade postgres",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        HttpResponseMessage persisted;

        do
        {
            persisted = await client.GetAsync($"/api/v1/log-entries/{id}");

            if (persisted.StatusCode == HttpStatusCode.OK)
            {
                break;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        persisted.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await persisted.Content.ReadFromJsonAsync<JsonElement>(Json);
        dto.GetProperty("message").GetString().Should().Be("paridade postgres");
    }
}
