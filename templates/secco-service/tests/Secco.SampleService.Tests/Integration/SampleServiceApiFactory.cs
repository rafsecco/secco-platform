using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.SharedKernel.Constants;
using Testcontainers.MsSql;
using Xunit;

namespace Secco.SampleService.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV)
/// com um SQL Server real via Testcontainers (ADR-0012) e dois tenants no catálogo
/// apontando para bancos distintos (ADR-0005).
/// </summary>
public sealed class SampleServiceApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string SigningKey = "chave-de-testes-com-32-caracteres!!";

    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _migrated;

    public Guid TenantAlfa { get; } = Guid.NewGuid();

    public Guid TenantBeta { get; } = Guid.NewGuid();

    public string GetTenantConnectionString(string databaseName) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName,
        }.ConnectionString;

    /// <summary>Gera um token HS256 compatível com a configuração de testes.</summary>
    public static string CreateToken(Guid tenantId, string subject = "test-user") =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "secco-tests",
            Audience = "secco-sampleservice",
            Claims = new Dictionary<string, object>
            {
                [SeccoClaims.Subject] = subject,
                [SeccoClaims.TenantId] = tenantId.ToString(),
            },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });

    /// <summary>Aplica as migrations nos bancos de tenant uma única vez por factory.</summary>
    public async Task EnsureTenantDatabasesMigratedAsync()
    {
        await _migrationLock.WaitAsync();

        try
        {
            if (!_migrated)
            {
                await Secco.SampleService.Infrastructure.SampleServiceInfrastructureExtensions
                    .MigrateSampleServiceTenantDatabasesAsync(Services);
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
                ["Secco:Authentication:Audience"] = "secco-sampleservice",
                ["Secco:Authentication:Issuer"] = "secco-tests",
                ["Secco:Authentication:DevelopmentSigningKey"] = SigningKey,
                [$"Secco:Tenancy:Tenants:{TenantAlfa}:ConnectionString"] =
                    GetTenantConnectionString("secco_sampleservice_alfa"),
                [$"Secco:Tenancy:Tenants:{TenantBeta}:ConnectionString"] =
                    GetTenantConnectionString("secco_sampleservice_beta"),
            }));
    }

    public async Task InitializeAsync() => await _container.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}
