using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SDK.AspNetCore.Correlation;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de DI para correlação de requisições (ADR-0004).</summary>
public static class SeccoCorrelationServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="ICorrelationContext"/> com tempo de vida <c>Scoped</c>.
    /// Requer <c>UseSeccoCorrelation()</c> no pipeline para ser populado.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSeccoCorrelation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<CorrelationContext>();
        services.TryAddScoped<ICorrelationContext>(sp => sp.GetRequiredService<CorrelationContext>());

        return services;
    }
}
