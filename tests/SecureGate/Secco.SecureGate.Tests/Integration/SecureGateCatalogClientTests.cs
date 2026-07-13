using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SecureGate.Client.Catalog;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O <see cref="SecureGateTenantCatalog"/> do pacote Client contra o SecureGate REAL
/// (tokens client credentials de verdade): cache com TTL, stale em falha, negativo
/// cacheado, indisponibilidade sem cache → <see cref="TenantCatalogUnavailableException"/>
/// e o fallback para o catálogo por configuração quando a seção não existe.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class SecureGateCatalogClientTests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
    private const string ServiceClientId = "catalog-client-tests";
    private const string ServiceClientSecret = "catalog-client-tests-secret-32char!!";

    private readonly List<Guid> _createdTenants = [];
    private readonly List<ServiceProvider> _providers = [];

    public async Task InitializeAsync()
    {
        await secureGate.EnsureDatabaseMigratedAsync();
        await secureGate.CreateClientAsync(ServiceClientId, ServiceClientSecret, "catalog:logstream");
    }

    public async Task DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }

        // Desativa os tenants do teste: o E2E da mesma collection itera o catálogo ATIVO
        // para aplicar migrations — entradas fabricadas aqui não podem vazar para lá
        using var scope = secureGate.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

        foreach (var tenantId in _createdTenants)
        {
            (await context.Tenants.FirstAsync(t => t.Id == tenantId)).Deactivate();
        }

        await context.SaveChangesAsync();
    }

    /// <summary>Injeta falha de rede sob demanda entre o catálogo e o TestServer.</summary>
    private sealed class FaultInjectingHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Fail
                ? throw new HttpRequestException("Falha injetada pelo teste.")
                : base.SendAsync(request, cancellationToken);
    }

    private ITenantCatalog BuildCatalog(
        FaultInjectingHandler fault,
        int cacheTtlSeconds = 300,
        IReadOnlyDictionary<string, string?>? configOverride = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configOverride ?? new Dictionary<string, string?>
            {
                ["Secco:SecureGate:BaseUrl"] = "http://localhost",
                ["Secco:SecureGate:ClientId"] = ServiceClientId,
                ["Secco:SecureGate:ClientSecret"] = ServiceClientSecret,
                ["Secco:SecureGate:Product"] = "logstream",
                ["Secco:SecureGate:CacheTtlSeconds"] = cacheTtlSeconds.ToString(CultureInfo.InvariantCulture),
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddSecureGateTenantCatalog();
        services.AddHttpClient(SecureGateTenantCatalog.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => fault);

        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        return provider.GetRequiredService<ITenantCatalog>();
    }

    private FaultInjectingHandler CreateFaultHandler() => new(secureGate.Server.CreateHandler());

    private async Task<Guid> CreateCatalogEntryAsync(string connectionString)
    {
        using var scope = secureGate.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

        var tenant = new Tenant("Tenant do client", $"t-{Guid.NewGuid():N}");
        context.Tenants.Add(tenant);
        context.TenantDatabases.Add(new TenantDatabase(tenant.Id, "logstream", connectionString));
        await context.SaveChangesAsync();

        _createdTenants.Add(tenant.Id);

        return tenant.Id;
    }

    private async Task UpdateConnectionStringAsync(Guid tenantId, string connectionString)
    {
        using var scope = secureGate.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

        var database = await context.TenantDatabases
            .FirstAsync(d => d.TenantId == tenantId && d.Product == "logstream");
        database.UpdateConnectionString(connectionString);

        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task FindAsync_ResolvesEntryAndCachesWithinTtl()
    {
        var tenantId = await CreateCatalogEntryAsync("Server=v1;Database=cache;");
        var catalog = BuildCatalog(CreateFaultHandler());

        var first = await catalog.FindAsync(tenantId);
        first!.ConnectionString.Should().Be("Server=v1;Database=cache;");

        await UpdateConnectionStringAsync(tenantId, "Server=v2;Database=cache;");

        var second = await catalog.FindAsync(tenantId);
        second!.ConnectionString.Should().Be("Server=v1;Database=cache;",
            "dentro do TTL a entrada vem do cache — sem nova chamada ao SecureGate");
    }

    [Fact]
    public async Task FindAsync_AfterTtl_RefreshesEntry()
    {
        var tenantId = await CreateCatalogEntryAsync("Server=v1;Database=ttl;");
        var catalog = BuildCatalog(CreateFaultHandler(), cacheTtlSeconds: 1);

        (await catalog.FindAsync(tenantId))!.ConnectionString.Should().Be("Server=v1;Database=ttl;");

        await UpdateConnectionStringAsync(tenantId, "Server=v2;Database=ttl;");
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        (await catalog.FindAsync(tenantId))!.ConnectionString.Should().Be("Server=v2;Database=ttl;",
            "expirado o TTL, a entrada é renovada no SecureGate (rotação de credencial propaga)");
    }

    [Fact]
    public async Task FindAsync_WhenSecureGateIsDown_ServesStaleEntry()
    {
        var tenantId = await CreateCatalogEntryAsync("Server=stale;Database=x;");
        var fault = CreateFaultHandler();
        var catalog = BuildCatalog(fault, cacheTtlSeconds: 1);

        (await catalog.FindAsync(tenantId))!.ConnectionString.Should().Be("Server=stale;Database=x;");

        fault.Fail = true;
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        var stale = await catalog.FindAsync(tenantId);
        stale!.ConnectionString.Should().Be("Server=stale;Database=x;",
            "SecureGate indisponível não derruba produto que já resolveu o tenant (decisão da 6.3)");
    }

    [Fact]
    public async Task FindAsync_UnknownTenantWithSecureGateDown_ThrowsUnavailable()
    {
        var fault = CreateFaultHandler();
        var catalog = BuildCatalog(fault);

        fault.Fail = true;

        var act = async () => await catalog.FindAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<TenantCatalogUnavailableException>(
            "sem cache não há o que servir — o pipeline de tenancy converte em 503 + Retry-After");
    }

    [Fact]
    public async Task FindAsync_UnknownTenant_ReturnsNull()
    {
        var catalog = BuildCatalog(CreateFaultHandler());

        (await catalog.FindAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ReturnsActiveEntries()
    {
        var tenantId = await CreateCatalogEntryAsync("Server=list;Database=x;");
        var catalog = BuildCatalog(CreateFaultHandler());

        var entries = await catalog.ListAsync();

        entries.Should().Contain(entry => entry.TenantId == tenantId);
    }

    [Fact]
    public async Task WithoutConfiguration_FallsBackToConfigurationCatalog()
    {
        var tenantId = Guid.NewGuid();
        var catalog = BuildCatalog(CreateFaultHandler(), configOverride: new Dictionary<string, string?>
        {
            // Sem a seção Secco:SecureGate — o catálogo por configuração do SDK assume
            [$"Secco:Tenancy:Tenants:{tenantId}:ConnectionString"] = "Server=config;Database=dev;",
        });

        catalog.Should().BeOfType<ConfigurationTenantCatalog>("sem a seção, o comportamento de DEV não muda");
        (await catalog.FindAsync(tenantId))!.ConnectionString.Should().Be("Server=config;Database=dev;");
    }

    [Fact]
    public void WithPartialConfiguration_FailsFast()
    {
        var fault = CreateFaultHandler();

        var act = () => BuildCatalog(fault, configOverride: new Dictionary<string, string?>
        {
            // Só a BaseUrl: nunca degrada silenciosamente para o catálogo por configuração
            ["Secco:SecureGate:BaseUrl"] = "http://localhost",
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*parcialmente configurado*");
    }
}
