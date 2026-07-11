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

    public Guid TenantAlfa { get; } = Guid.NewGuid();

    public Guid TenantBeta { get; } = Guid.NewGuid();

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
                [$"Secco:Tenancy:Tenants:{TenantAlfa}:ConnectionString"] =
                    GetTenantConnectionString("secco_logstream_alfa"),
                [$"Secco:Tenancy:Tenants:{TenantBeta}:ConnectionString"] =
                    GetTenantConnectionString("secco_logstream_beta"),
            }));
    }

    public async Task InitializeAsync() => await _container.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}
