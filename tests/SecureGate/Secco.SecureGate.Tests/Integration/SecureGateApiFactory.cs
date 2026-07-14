using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Sobe a API real (ambiente <c>Testing</c> — sem migrations/seed automáticos de DEV)
/// com um SQL Server real via Testcontainers (ADR-0012) hospedando o banco de
/// PLATAFORMA <c>secco_securegate</c> (ADR-0022 — identidade não é dado de tenant).
/// Herdável: <see cref="SelfIssuedAuthSecureGateApiFactory"/> troca a chave HS256 de
/// testes pela Authority do próprio servidor.
/// </summary>
public class SecureGateApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _migrated;

    public string GetPlatformConnectionString() => GetConnectionStringFor("secco_securegate");

    /// <summary>Connection string para um banco adicional no mesmo container (ex.: tenant de outro produto no E2E).</summary>
    public string GetConnectionStringFor(string databaseName) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName,
        }.ConnectionString;

    /// <summary>Aplica migrations + seed de referência (scopes) uma única vez por factory.</summary>
    public async Task EnsureDatabaseMigratedAsync()
    {
        await _migrationLock.WaitAsync();

        try
        {
            if (!_migrated)
            {
                await Secco.SecureGate.Infrastructure.SecureGateInfrastructureExtensions
                    .MigrateSecureGateDatabaseAsync(Services);

                // Seed de referência (scopes de produto); o de DEV não roda em Testing (guarda dupla)
                await Secco.SDK.EntityFrameworkCore.Seeding.SeccoSeedingExtensions
                    .SeedSeccoDataAsync(Services);

                _migrated = true;
            }
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    /// <summary>Registra um client OIDC de teste (client credentials) com os scopes informados.</summary>
    public Task CreateClientAsync(string clientId, string clientSecret, params string[] scopes) =>
        CreateClientWithRolesAsync(clientId, clientSecret, roles: null, scopes);

    /// <summary>
    /// Registra um client OIDC de teste com scopes e roles (Fase 6.4, ADR-0021 —
    /// máquinas carregam a claim curta <c>role</c> como os usuários). Nome distinto por
    /// design: um overload posicional confundiria scope com roles.
    /// </summary>
    public async Task CreateClientWithRolesAsync(string clientId, string clientSecret, string? roles, params string[] scopes)
    {
        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();

        if (await applications.FindByClientIdAsync(clientId) is not null)
        {
            return;
        }

        var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            DisplayName = $"Client de teste {clientId}",
        };

        descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);

        foreach (var scopeName in scopes)
        {
            descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
        }

        await applications.CreateAsync(descriptor);

        if (!string.IsNullOrWhiteSpace(roles)
            && await applications.FindByClientIdAsync(clientId)
                is Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication application)
        {
            application.Roles = roles;
            await applications.UpdateAsync(application);
        }
    }

    /// <summary>
    /// Registra um client PÚBLICO de teste (authorization code + PKCE + refresh, sem secret) —
    /// o modelo de uma aplicação web/SPA (Fase 6.5). Consent implícito (first-party).
    /// </summary>
    public async Task CreatePublicClientAsync(string clientId, string redirectUri, params string[] scopes)
    {
        using var scope = Services.CreateScope();
        var applications = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();

        if (await applications.FindByClientIdAsync(clientId) is not null)
        {
            return;
        }

        var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Public,
            ConsentType = OpenIddict.Abstractions.OpenIddictConstants.ConsentTypes.Implicit,
            DisplayName = $"Client público de teste {clientId}",
            RedirectUris = { new Uri(redirectUri) },
            Permissions =
            {
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.EndSession,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Email,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddict.Abstractions.OpenIddictConstants.Permissions.Scopes.Roles,
            },
        };

        foreach (var scopeName in scopes)
        {
            descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
        }

        await applications.CreateAsync(descriptor);
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
