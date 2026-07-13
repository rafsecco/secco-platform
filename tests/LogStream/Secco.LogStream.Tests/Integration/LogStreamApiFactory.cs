using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV)
/// com um SQL Server real via Testcontainers (ADR-0012) e dois tenants no catálogo
/// apontando para bancos distintos do mesmo container (ADR-0005).
/// </summary>
public sealed class LogStreamApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _migrated;

    public Guid TenantAlfa { get; } = Guid.NewGuid();

    public Guid TenantBeta { get; } = Guid.NewGuid();

    /// <summary>Aplica as migrations nos bancos de tenant uma única vez por factory.</summary>
    public async Task EnsureTenantDatabasesMigratedAsync()
    {
        await _migrationLock.WaitAsync();

        try
        {
            if (!_migrated)
            {
                await Secco.LogStream.Infrastructure.LogStreamInfrastructureExtensions
                    .MigrateLogStreamTenantDatabasesAsync(Services);
                _migrated = true;
            }
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    public string GetTenantConnectionString(string databaseName) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName,
        }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Secco:Authentication:Audience"] = "secco-logstream",
                ["Secco:Authentication:Issuer"] = "secco-tests",
                ["Secco:Authentication:DevelopmentSigningKey"] = "chave-de-testes-com-32-caracteres!!",
                [$"Secco:Tenancy:Tenants:{TenantAlfa}:ConnectionString"] =
                    GetTenantConnectionString("secco_logstream_alfa"),
                [$"Secco:Tenancy:Tenants:{TenantBeta}:ConnectionString"] =
                    GetTenantConnectionString("secco_logstream_beta"),
                // Permissões do role dos tokens de teste (Fase 6.4, ADR-0021) — resolver por configuração
                ["Secco:Authorization:Roles:test-admin:Permissions:0"] = "log-entries:read",
                ["Secco:Authorization:Roles:test-admin:Permissions:1"] = "log-entries:write",
                ["Secco:Authorization:Roles:test-admin:Permissions:2"] = "log-processes:read",
                ["Secco:Authorization:Roles:test-admin:Permissions:3"] = "log-processes:write",
                ["Secco:Authorization:Roles:test-admin:Permissions:4"] = "api-call-logs:read",
                ["Secco:Authorization:Roles:test-admin:Permissions:5"] = "api-call-logs:write",
            }));
    }

    public async Task InitializeAsync() => await _container.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}
