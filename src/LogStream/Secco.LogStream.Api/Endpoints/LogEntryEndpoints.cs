using Secco.LogStream.Api.Requests;
using Secco.LogStream.Application;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Domain.LogEntries;
using Secco.SDK.AspNetCore.Correlation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Api.Endpoints;

/// <summary>
/// Endpoints de registros de log (<c>/api/v1/log-entries</c>, ADR-0010). Autorização
/// granular por permissão (Fase 6.4, ADR-0021): ingestão exige <c>log-entries:write</c>
/// e consulta <c>log-entries:read</c>, resolvidas do role do token em runtime.
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
			.RequireAuthorization(LogStreamPermissions.LogEntries.Write)
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
			.RequireAuthorization(LogStreamPermissions.LogEntries.Write)
			.WithSummary("Registra um lote de logs (ingestão assíncrona; headers aplicados a todos os itens).")
			.Produces<LogEntryBatchAcceptedResponse>(StatusCodes.Status202Accepted)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		group.MapGet("/{id:guid}", async (Guid id, GetLogEntryByIdHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(id, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.RequireAuthorization(LogStreamPermissions.LogEntries.Read)
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
				string? serviceName,
				string? category,
				int? page,
				int? size) =>
			(await handler.HandleAsync(
				new LogEntrySearchCriteria(from, to, level, message, correlationId, serviceName, category,
					new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize)),
				cancellationToken))
				.ToHttpResult(result => Results.Ok(result)))
			.RequireAuthorization(LogStreamPermissions.LogEntries.Read)
			.WithSummary("Busca paginada de registros de log (filtros opcionais, mais recentes primeiro).")
			.Produces<PagedResult<LogEntryDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		return endpoints;
	}

	/// <summary>
	/// O valor de correlação do payload vence quando presente; o header <c>X-Correlation-Id</c>
	/// só é usado como fallback. É o que torna o <c>/batch</c> utilizável por um sink que
	/// acumula logs de requisições diferentes — antes, o header era aplicado a todos os itens.
	/// </summary>
	private static CreateLogEntryCommand ToCommand(CreateLogEntryRequest request, ICorrelationContext correlation) =>
		new(request.Level,
			request.Message,
			request.StackTrace,
			request.CorrelationId
				?? (Guid.TryParse(correlation.CorrelationId, out var correlationId) ? correlationId : null),
			request.ServiceName,
			request.Category);
}
