using Microsoft.Extensions.DependencyInjection;

namespace Secco.SecureGate.Application;

/// <summary>Composição de DI da camada de aplicação do SecureGate.</summary>
public static class SecureGateApplicationExtensions
{
    /// <summary>Registra os casos de uso (handlers chegam com as fases 6.2+).</summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSecureGateApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services;
    }
}
