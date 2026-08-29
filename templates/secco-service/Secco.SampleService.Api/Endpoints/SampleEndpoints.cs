using Secco.SampleService.Application;
using Secco.SampleService.Application.Samples;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Pagination;

namespace Secco.SampleService.Api.Endpoints;

/// <summary>Payload de criação de um sample.</summary>
/// <param name="Name">Nome. Obrigatório.</param>
/// <param name="Description">Descrição livre, quando houver.</param>
public sealed record CreateSampleRequest(string? Name, string? Description = null);

/// <summary>
/// Endpoints de EXEMPLO (<c>/api/v1/samples</c>, ADR-0010) — demonstram o padrão da borda:
/// autorização granular por permissão (ADR-0021): escrita exige <c>samples:write</c> e
/// consulta <c>samples:read</c>, resolvidas do role do token em runtime. <c>Result&lt;T&gt;</c>
/// convertido via <c>ToHttpResult()</c> (ProblemDetails automático), paginação da plataforma.
/// Apague junto com o restante do recurso Sample.
/// </summary>
public static class SampleEndpoints
{
	/// <summary>Mapeia os endpoints de samples.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapSampleEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/samples").WithTags("Samples");

		group.MapPost("/", async (CreateSampleRequest request, CreateSampleHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(new CreateSampleCommand(request.Name, request.Description), cancellationToken))
				.ToHttpResult(dto => Results.Created($"/api/v1/samples/{dto.Id}", dto)))
			.RequireAuthorization(SampleServicePermissions.Samples.Write)
			.WithSummary("Cria um sample.")
			.Produces<SampleDto>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		group.MapGet("/{id:guid}", async (Guid id, GetSampleByIdHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(id, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.RequireAuthorization(SampleServicePermissions.Samples.Read)
			.WithSummary("Busca um sample pelo identificador.")
			.Produces<SampleDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/", async (
				SearchSamplesHandler handler,
				CancellationToken cancellationToken,
				string? name,
				int? page,
				int? size) =>
			(await handler.HandleAsync(
				new SampleSearchCriteria(name,
					new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize)),
				cancellationToken))
				.ToHttpResult(result => Results.Ok(result)))
			.RequireAuthorization(SampleServicePermissions.Samples.Read)
			.WithSummary("Busca paginada de samples, mais recentes primeiro.")
			.Produces<PagedResult<SampleDto>>(StatusCodes.Status200OK);

		return endpoints;
	}
}
