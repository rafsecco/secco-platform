using Secco.LogStream.Api.Requests;
using Secco.LogStream.Application.LogProcesses;
using Secco.LogStream.Domain.LogProcesses;
using Secco.SDK.AspNetCore.Correlation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Api.Endpoints;

/// <summary>
/// Endpoints de processos (<c>/api/v1/log-processes</c>): a listagem JÁ é a auditoria —
/// o status agregado vem sempre computado e é filtrável. Details são recurso aninhado
/// do processo. Ingestão assíncrona (<c>202</c>) para processo e details.
/// </summary>
public static class LogProcessEndpoints
{
    /// <summary>Mapeia os endpoints de processos.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapLogProcessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/log-processes").WithTags("LogProcesses");

        group.MapPost("/", (
                CreateLogProcessRequest request,
                CreateLogProcessHandler handler,
                ICorrelationContext correlation) =>
            handler.Handle(new CreateLogProcessCommand(request.Name, request.ExternalReference, ParseCorrelation(correlation)))
                .ToHttpResult(id => Results.Accepted($"/api/v1/log-processes/{id}", new LogEntryAcceptedResponse(id))))
            .WithSummary("Cria um processo (ingestão assíncrona) — o Id devolvido já serve para enviar details.")
            .Produces<LogEntryAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{id:guid}", async (Guid id, GetLogProcessByIdHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken))
                .ToHttpResult(dto => Results.Ok(dto)))
            .WithSummary("Busca um processo pelo identificador, com status agregado e contagem de details.")
            .Produces<LogProcessDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
                SearchLogProcessesHandler handler,
                CancellationToken cancellationToken,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? name,
                ProcessStatus? status,
                Guid? correlationId,
                int? page,
                int? size) =>
            (await handler.HandleAsync(
                new LogProcessSearchCriteria(from, to, name, status, correlationId,
                    new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize)),
                cancellationToken))
                .ToHttpResult(result => Results.Ok(result)))
            .WithSummary("Busca paginada de processos com status agregado (a auditoria) — filtrável por status.")
            .Produces<PagedResult<LogProcessDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/details", (
                Guid id,
                CreateLogProcessDetailRequest request,
                CreateLogProcessDetailHandler handler,
                ICorrelationContext correlation) =>
            handler.Handle(id, ToDetailCommand(request, correlation))
                .ToHttpResult(detailId => Results.Accepted($"/api/v1/log-processes/{id}/details", new LogEntryAcceptedResponse(detailId))))
            .WithSummary("Registra um detail do processo (ingestão assíncrona).")
            .Produces<LogEntryAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{id:guid}/details/batch", (
                Guid id,
                List<CreateLogProcessDetailRequest> requests,
                CreateLogProcessDetailHandler handler,
                ICorrelationContext correlation) =>
            handler.HandleBatch(id, requests.Select(request => ToDetailCommand(request, correlation)).ToList())
                .ToHttpResult(ids => Results.Accepted($"/api/v1/log-processes/{id}/details", new LogEntryBatchAcceptedResponse(ids))))
            .WithSummary("Registra um lote de details do processo (ingestão assíncrona).")
            .Produces<LogEntryBatchAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{id:guid}/details", async (
                Guid id,
                GetLogProcessDetailsHandler handler,
                CancellationToken cancellationToken,
                int? page,
                int? size) =>
            (await handler.HandleAsync(
                id,
                new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize),
                cancellationToken))
                .ToHttpResult(result => Results.Ok(result)))
            .WithSummary("Busca paginada dos details de um processo, mais recentes primeiro.")
            .Produces<PagedResult<LogProcessDetailDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static CreateLogProcessDetailCommand ToDetailCommand(CreateLogProcessDetailRequest request, ICorrelationContext correlation) =>
        new(request.Level, request.Message, request.StackTrace, ParseCorrelation(correlation));

    private static Guid? ParseCorrelation(ICorrelationContext correlation) =>
        Guid.TryParse(correlation.CorrelationId, out var correlationId) ? correlationId : null;
}
