using Secco.LogStream.Api.Requests;
using Secco.LogStream.Application;
using Secco.LogStream.Application.ApiCalls;
using Secco.SDK.AspNetCore.Correlation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Api.Endpoints;

/// <summary>
/// Endpoints de chamadas de API (<c>/api/v1/api-call-logs</c>): diagnóstico de integrações.
/// Headers são sanitizados no servidor (blocklist — ADR-0020) e bodies truncados no limite.
/// </summary>
public static class ApiCallLogEndpoints
{
    /// <summary>Mapeia os endpoints de chamadas de API.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapApiCallLogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/api-call-logs").WithTags("ApiCallLogs");

        group.MapPost("/", (
                CreateApiCallLogRequest request,
                CreateApiCallLogHandler handler,
                ICorrelationContext correlation) =>
            handler.Handle(new CreateApiCallLogCommand(
                    request.Url,
                    request.HttpMethod,
                    request.IsSuccess,
                    request.RequestBody,
                    request.RequestHeaders,
                    request.ResponseStatusCode,
                    request.ResponseBody,
                    request.DurationMs,
                    request.ErrorMessage,
                    Guid.TryParse(correlation.CorrelationId, out var correlationId) ? correlationId : null))
                .ToHttpResult(id => Results.Accepted($"/api/v1/api-call-logs/{id}", new LogEntryAcceptedResponse(id))))
            .RequireAuthorization(LogStreamPermissions.ApiCallLogs.Write)
            .WithSummary("Registra uma chamada de API externa (ingestão assíncrona; headers sensíveis são redigidos no servidor).")
            .Produces<LogEntryAcceptedResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{id:guid}", async (Guid id, GetApiCallLogByIdHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken))
                .ToHttpResult(dto => Results.Ok(dto)))
            .RequireAuthorization(LogStreamPermissions.ApiCallLogs.Read)
            .WithSummary("Busca uma chamada de API pelo identificador.")
            .Produces<ApiCallLogDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
                SearchApiCallLogsHandler handler,
                CancellationToken cancellationToken,
                DateTimeOffset? from,
                DateTimeOffset? to,
                bool? isSuccess,
                string? method,
                string? url,
                int? statusCode,
                Guid? correlationId,
                int? page,
                int? size) =>
            (await handler.HandleAsync(
                new ApiCallLogSearchCriteria(from, to, isSuccess, method, url, statusCode, correlationId,
                    new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize)),
                cancellationToken))
                .ToHttpResult(result => Results.Ok(result)))
            .RequireAuthorization(LogStreamPermissions.ApiCallLogs.Read)
            .WithSummary("Busca paginada de chamadas de API (filtros opcionais, mais recentes primeiro).")
            .Produces<PagedResult<ApiCallLogDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
