using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Secco.NotificationHub.Infrastructure.Email;
using Secco.SharedKernel.Constants;
using Testcontainers.MsSql;
using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV)
/// com um SQL Server real via Testcontainers (ADR-0012): bancos de tenant (ADR-0005) e o
/// banco de plataforma do Hangfire (ADR-0015), todos no mesmo container. O envio de
/// e-mail é substituído por <see cref="FakeEmailSender"/> — não há SMTP real em teste.
/// </summary>
public sealed class NotificationHubApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string SigningKey = "chave-de-testes-com-32-caracteres!!";
    private const string TestRole = "test-admin";
    private const string PlatformDatabaseName = "secco_notificationhub_platform";

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
    public static string CreateToken(Guid tenantId, string subject = "test-user", string role = TestRole) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "secco-tests",
            Audience = "secco-notificationhub",
            Claims = new Dictionary<string, object>
            {
                [SeccoClaims.Subject] = subject,
                [SeccoClaims.TenantId] = tenantId.ToString(),
                [SeccoClaims.Role] = role,
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
                await Secco.NotificationHub.Infrastructure.NotificationHubInfrastructureExtensions
                    .MigrateNotificationHubTenantDatabasesAsync(Services);
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
                ["Secco:Authentication:Audience"] = "secco-notificationhub",
                ["Secco:Authentication:Issuer"] = "secco-tests",
                ["Secco:Authentication:DevelopmentSigningKey"] = SigningKey,
                [$"Secco:Tenancy:Tenants:{TenantAlfa}:ConnectionString"] =
                    GetTenantConnectionString("secco_notificationhub_alfa"),
                [$"Secco:Tenancy:Tenants:{TenantBeta}:ConnectionString"] =
                    GetTenantConnectionString("secco_notificationhub_beta"),
                // Permissões do role dos tokens de teste (ADR-0021) — resolver por configuração
                [$"Secco:Authorization:Roles:{TestRole}:Permissions:0"] = "notifications:read",
                [$"Secco:Authorization:Roles:{TestRole}:Permissions:1"] = "notifications:write",
                // Banco de PLATAFORMA do Hangfire (ADR-0015) — nunca por tenant
                ["NotificationHub:BackgroundJobs:ConnectionString"] =
                    GetTenantConnectionString(PlatformDatabaseName),
            }));

        builder.ConfigureTestServices(services =>
        {
            // Sem SMTP real em teste: substitui o sender real pelo fake (ADR-0012)
            services.AddScoped<IEmailSender, FakeEmailSender>();
        });
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Diferente das migrations EF (que criam o banco de tenant sozinhas), o Hangfire
        // só cria o SCHEMA dentro de um banco já existente — o banco de plataforma
        // precisa existir antes do primeiro Enqueue.
        await using var connection = new SqlConnection(_container.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"IF DB_ID('{PlatformDatabaseName}') IS NULL CREATE DATABASE [{PlatformDatabaseName}]";
        await command.ExecuteNonQueryAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _container.DisposeAsync();
    }
}
