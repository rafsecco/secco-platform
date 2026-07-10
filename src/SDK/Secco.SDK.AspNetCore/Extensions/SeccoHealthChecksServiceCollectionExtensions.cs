using Microsoft.Extensions.DependencyInjection;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de DI para health checks padronizados (ADR-0004).</summary>
public static class SeccoHealthChecksServiceCollectionExtensions
{
    /// <summary>
    /// Registra a infraestrutura de health checks e devolve o <see cref="IHealthChecksBuilder"/>
    /// para o produto encadear os checks das suas dependências (SQL do tenant, filas etc.) —
    /// eles afetam apenas o <c>/health/ready</c>. Requer <c>MapSeccoHealthChecks()</c> nos endpoints.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IHealthChecksBuilder AddSeccoHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddHealthChecks();
    }
}
