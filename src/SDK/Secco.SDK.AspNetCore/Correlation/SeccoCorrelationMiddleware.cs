using Microsoft.AspNetCore.Http;
using Secco.SharedKernel.Constants;

namespace Secco.SDK.AspNetCore.Correlation;

/// <summary>
/// Resolve o correlation id da requisição (ADR-0004): reaproveita o <c>X-Correlation-Id</c>
/// recebido se for um Guid válido (ADR-0020), senão gera um novo Guid v7; popula o
/// <see cref="ICorrelationContext"/> do escopo e devolve o mesmo id no header de resposta.
/// </summary>
public sealed class SeccoCorrelationMiddleware(RequestDelegate next)
{
    /// <summary>Executa a resolução do correlation id e invoca o próximo delegate do pipeline.</summary>
    /// <param name="context">Contexto HTTP da requisição atual.</param>
    /// <param name="correlationContext">Contexto de correlação do escopo (injetado pelo DI).</param>
    public async Task InvokeAsync(HttpContext context, CorrelationContext correlationContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(correlationContext);

        var receivedValue = context.Request.Headers[SeccoHeaders.CorrelationId].ToString();
        var correlationId = CorrelationIdParser.TryParse(receivedValue, out var parsed)
            ? parsed
            : Guid.CreateVersion7();

        correlationContext.CorrelationId = correlationId.ToString();

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[SeccoHeaders.CorrelationId] = correlationId.ToString();
            return Task.CompletedTask;
        });

        await next(context).ConfigureAwait(false);
    }
}
