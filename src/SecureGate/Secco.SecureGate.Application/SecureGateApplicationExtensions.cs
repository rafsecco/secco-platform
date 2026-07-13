using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Authorization;
using Secco.SecureGate.Application.Catalog;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Tenants;

namespace Secco.SecureGate.Application;

/// <summary>Composição de DI da camada de aplicação do SecureGate.</summary>
public static class SecureGateApplicationExtensions
{
    /// <summary>Registra os casos de uso do catálogo de tenants (Fase 6.3).</summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSecureGateApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Gestão de tenants (scope securegate:admin)
        services.AddScoped<CreateTenantHandler>();
        services.AddScoped<ListTenantsHandler>();
        services.AddScoped<GetTenantHandler>();
        services.AddScoped<SetTenantActivationHandler>();
        services.AddScoped<UpsertTenantDatabaseHandler>();

        // Catálogo servido aos produtos (scope catalog:<produto>)
        services.AddScoped<GetCatalogTenantHandler>();
        services.AddScoped<ListCatalogTenantsHandler>();

        // Roles + permissões (Fase 6.4, ADR-0021): gestão e resolução
        services.AddScoped<CreateRoleHandler>();
        services.AddScoped<ListRolesHandler>();
        services.AddScoped<SetRolePermissionsHandler>();
        services.AddScoped<GetRolePermissionsHandler>();

        return services;
    }
}
