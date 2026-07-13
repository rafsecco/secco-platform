using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Testcontainers.MsSql;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV)
/// com um SQL Server real via Testcontainers (ADR-0012) hospedando o banco de
/// PLATAFORMA <c>secco_securegate</c> (ADR-0022 — identidade não é dado de tenant).
/// </summary>
public sealed class SecureGateApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _migrated;

    public string GetPlatformConnectionString() =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "secco_securegate",
        }.ConnectionString;

    /// <summary>Aplica as migrations no banco de plataforma uma única vez por factory.</summary>
    public async Task EnsureDatabaseMigratedAsync()
    {
        await _migrationLock.WaitAsync();

        try
        {
            if (!_migrated)
            {
                await Secco.SecureGate.Infrastructure.SecureGateInfrastructureExtensions
                    .MigrateSecureGateDatabaseAsync(Services);
                _migrated = true;
            }
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecureGate:Database:ConnectionString"] = GetPlatformConnectionString(),
                ["Secco:Authentication:Audience"] = "secco-securegate",
                ["Secco:Authentication:Issuer"] = "secco-tests",
                ["Secco:Authentication:DevelopmentSigningKey"] = "chave-de-testes-com-32-caracteres!!",
            }));
    }

    public async Task InitializeAsync() => await _container.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}
