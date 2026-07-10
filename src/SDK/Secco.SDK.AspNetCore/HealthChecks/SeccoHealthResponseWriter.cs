using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Secco.SDK.AspNetCore.HealthChecks;

/// <summary>
/// Escreve o relatório de readiness em JSON com nome, status e duração de cada check —
/// e nada além disso: descrições e mensagens de exceção ficam fora da resposta (ADR-0020;
/// mensagens de erro de infraestrutura vazam hostnames e connection strings). O diagnóstico
/// detalhado pertence aos logs.
/// </summary>
internal static class SeccoHealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                durationMs = entry.Value.Duration.TotalMilliseconds,
            }),
        };

        return context.Response.WriteAsJsonAsync(payload, context.RequestAborted);
    }
}
