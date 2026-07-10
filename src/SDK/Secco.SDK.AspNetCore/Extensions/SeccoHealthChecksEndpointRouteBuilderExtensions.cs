using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Secco.SDK.AspNetCore.HealthChecks;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de endpoints para health checks padronizados (ADR-0004).</summary>
public static class SeccoHealthChecksEndpointRouteBuilderExtensions
{
    /// <summary>Rota de liveness: o processo responde HTTP → está vivo.</summary>
    public const string LivenessPath = "/health/live";

    /// <summary>Rota de readiness: executa todos os checks registrados.</summary>
    public const string ReadinessPath = "/health/ready";

    /// <summary>
    /// Mapeia os endpoints padronizados da plataforma, anônimos (probes de orquestrador
    /// não autenticam):
    /// <c>/health/live</c> não executa nenhum check — reiniciar o processo não conserta
    /// dependência externa caída, então liveness só atesta que o processo responde;
    /// <c>/health/ready</c> executa todos os checks registrados e responde JSON sem
    /// detalhes sensíveis (nome, status e duração por check).
    /// Requer <c>AddSeccoHealthChecks()</c> registrado no DI.
    /// </summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapSeccoHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = _ => false,
        });

        endpoints.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            ResponseWriter = SeccoHealthResponseWriter.WriteAsync,
        });

        return endpoints;
    }
}
