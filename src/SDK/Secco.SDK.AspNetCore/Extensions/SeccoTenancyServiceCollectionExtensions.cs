using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de DI para multi-tenancy database-per-tenant (ADR-0005).</summary>
public static class SeccoTenancyServiceCollectionExtensions
{
    /// <summary>
    /// Registra <see cref="ITenantContext"/>, <see cref="ITenantCatalog"/> (implementação
    /// padrão sobre <c>IConfiguration</c>, seção <c>Secco:Tenancy:Tenants</c>) e
    /// <see cref="ITenantConnectionFactory"/>. Registros usam <c>TryAdd</c> — para substituir
    /// o catálogo, registre a implementação própria <b>antes</b> de chamar este método.
    /// Requer <c>UseSeccoTenancy()</c> no pipeline para o contexto ser populado.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSeccoTenancy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<TenantContext>();
        services.TryAddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.TryAddSingleton<ITenantCatalog, ConfigurationTenantCatalog>();
        services.TryAddScoped<ITenantConnectionFactory, TenantConnectionFactory>();

        return services;
    }
}
