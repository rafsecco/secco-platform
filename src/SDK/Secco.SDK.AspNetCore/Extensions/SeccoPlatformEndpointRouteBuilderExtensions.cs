using Microsoft.AspNetCore.Routing;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição completa de endpoints da plataforma (ADR-0004).</summary>
public static class SeccoPlatformEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Mapeia os endpoints padronizados da plataforma — hoje, os health checks
    /// (<c>/health/live</c> e <c>/health/ready</c>). Novos endpoints de plataforma
    /// entrarão aqui de forma aditiva.
    /// </summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapSeccoPlatform(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapSeccoHealthChecks();
    }
}
