using Secco.LogStream.Api.Requests;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Domain.LogEntries;
using Secco.SDK.AspNetCore.Correlation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Api.Endpoints;

/// <summary>
/// Endpoints de registros de log (<c>/api/v1/log-entries</c>, ADR-0010). Protegidos pela
/// <c>FallbackPolicy</c> da plataforma — nenhuma metadata de autorização é necessária.
/// Ingestão responde <c>202</c>: a persistência é assíncrona (fila + worker).
/// </summary>
public static class LogEntryEndpoints
{
    /// <summary>Mapeia os endpoints de registros de log.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapLogEntryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/log-entries").WithTags("LogEntries");

        group.MapPost("/", (
                CreateLogEntryRequest request,
                CreateLogEntryHandler handler,
                ICorrelationContext correlation) =>
            handler.Handle(ToCommand(request, correlation))
                .ToHttpResult(id => Results.Accepted($"/api/v1/log-entries/{id}", new LogEntryAcceptedResponse(id))))
            .WithSummary("Registra um log (ingestão assíncrona).")
            .Produces<LogEntryAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/batch", (
                List<CreateLogEntryRequest> requests,
                CreateLogEntryBatchHandler handler,
                ICorrelationContext correlation) =>
            handler.Handle(requests.Select(request => ToCommand(request, correlation)).ToList())
                .ToHttpResult(ids => Results.Accepted("/api/v1/log-entries", new LogEntryBatchAcceptedResponse(ids))))
            .WithSummary("Registra um lote de logs (ingestão assíncrona; headers aplicados a todos os itens).")
            .Produces<LogEntryBatchAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{id:guid}", async (Guid id, GetLogEntryByIdHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken))
                .ToHttpResult(dto => Results.Ok(dto)))
            .WithSummary("Busca um registro de log pelo identificador.")
            .Produces<LogEntryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
                SearchLogEntriesHandler handler,
                CancellationToken cancellationToken,
                DateTimeOffset? from,
                DateTimeOffset? to,
                LogEntryLevel? level,
                string? message,
                Guid? correlationId,
                int? page,
                int? size) =>
            (await handler.HandleAsync(
                new LogEntrySearchCriteria(from, to, level, message, correlationId,
                    new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize)),
                cancellationToken))
                .ToHttpResult(result => Results.Ok(result)))
            .WithSummary("Busca paginada de registros de log (filtros opcionais, mais recentes primeiro).")
            .Produces<PagedResult<LogEntryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return endpoints;
    }

    private static CreateLogEntryCommand ToCommand(CreateLogEntryRequest request, ICorrelationContext correlation) =>
        new(request.Level,
            request.Message,
            request.StackTrace,
            Guid.TryParse(correlation.CorrelationId, out var correlationId) ? correlationId : null);
}
