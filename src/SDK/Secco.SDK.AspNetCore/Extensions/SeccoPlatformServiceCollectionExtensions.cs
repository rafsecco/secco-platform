using Microsoft.Extensions.DependencyInjection;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição completa de DI da plataforma (ADR-0004).</summary>
public static class SeccoPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registra todo o comportamento transversal da plataforma: correlação, autenticação
    /// (exige a seção <c>Secco:Authentication</c> — fail-fast no startup), tenancy, health
    /// checks e resiliência HTTP. Sem toggles — a identidade única da ADR-0004.
    /// Chamadas repetidas são no-op. Ajuste fino: chamar a extensão individual
    /// (ex.: <c>AddSeccoResilience(o =&gt; ...)</c>) <b>antes</b> deste método; para
    /// registrar checks de readiness, chamar <c>AddSeccoHealthChecks()</c> a qualquer
    /// momento e encadear no builder devolvido.
    /// Composição de pipeline correspondente: <c>UseSeccoPlatform()</c> + <c>MapSeccoPlatform()</c>.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSeccoPlatform(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(SeccoPlatformMarker)))
        {
            return services;
        }

        services.AddSingleton<SeccoPlatformMarker>();

        services.AddSeccoCorrelation();
        services.AddSeccoAuthentication();
        services.AddSeccoTenancy();
        services.AddSeccoHealthChecks();
        services.AddSeccoResilience();

        return services;
    }

    /// <summary>Marcador de registro único da composição da plataforma.</summary>
    internal sealed class SeccoPlatformMarker;
}
