using Secco.LogStream.Api.Requests;
using Secco.LogStream.Application;
using Secco.LogStream.Application.Audit;
using Secco.SDK.AspNetCore.Correlation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Api.Endpoints;

/// <summary>
/// Endpoints da trilha de auditoria (<c>/api/v1/audit-entries</c>, ADR-0010). Autorização
/// granular por permissão (ADR-0021): ingestão exige <c>audit-entries:write</c> e consulta
/// <c>audit-entries:read</c>. Sem batch, sem PUT, sem DELETE — uma trilha que se pode editar
/// não é trilha.
/// <para>
/// <b>Ingestão SÍNCRONA — diferença deliberada frente aos outros recursos do LogStream.</b>
/// O POST grava e só então responde <c>201 Created</c>: um registro com obrigação legal não
/// pode desaparecer numa fila cheia sem que ninguém saiba. NÃO trocar por ingestão assíncrona
/// (ver <see cref="CreateAuditEntryHandler"/> e o design doc, seção "Trilha de auditoria").
/// </para>
/// </summary>
public static class AuditEntryEndpoints
{
	/// <summary>Mapeia os endpoints da trilha de auditoria.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapAuditEntryEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/audit-entries").WithTags("AuditEntries");

		group.MapPost("/", async (
				CreateAuditEntryRequest request,
				CreateAuditEntryHandler handler,
				ICorrelationContext correlation,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(ToCommand(request, correlation), cancellationToken))
				.ToHttpResult(dto => Results.Created($"/api/v1/audit-entries/{dto.Id}", dto)))
			.RequireAuthorization(LogStreamPermissions.AuditEntries.Write)
			.WithSummary("Registra uma entrada de auditoria (ingestão SÍNCRONA — decisão deliberada, ver documentação do produto).")
			.Produces<AuditEntryDto>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		group.MapGet("/{id:guid}", async (Guid id, GetAuditEntryByIdHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(id, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.RequireAuthorization(LogStreamPermissions.AuditEntries.Read)
			.WithSummary("Busca uma entrada de auditoria pelo identificador.")
			.Produces<AuditEntryDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/", async (
				SearchAuditEntriesHandler handler,
				CancellationToken cancellationToken,
				DateTimeOffset? from,
				DateTimeOffset? to,
				string? actorId,
				string? action,
				string? resourceType,
				string? resourceId,
				Guid? correlationId,
				int? page,
				int? size) =>
			(await handler.HandleAsync(
				new AuditEntrySearchCriteria(from, to, actorId, action, resourceType, resourceId, correlationId,
					new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize)),
				cancellationToken))
				.ToHttpResult(result => Results.Ok(result)))
			.RequireAuthorization(LogStreamPermissions.AuditEntries.Read)
			.WithSummary("Busca paginada de entradas de auditoria (filtros opcionais, mais recentes primeiro).")
			.Produces<PagedResult<AuditEntryDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		return endpoints;
	}

	private static CreateAuditEntryCommand ToCommand(CreateAuditEntryRequest request, ICorrelationContext correlation) =>
		new(request.ActorId,
			request.ActorType,
			request.Action,
			request.ActorName,
			request.ResourceType,
			request.ResourceId,
			request.Metadata,
			request.CorrelationId
				?? (Guid.TryParse(correlation.CorrelationId, out var correlationId) ? correlationId : null),
			request.OccurredAt);
}
