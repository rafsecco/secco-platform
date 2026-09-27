using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Leitura dos grupos do diretório federado de um tenant (issue #27, ADR-0036). Só leitura — o
/// mapeamento grupo→perfil é a #28, ainda não implementada.
/// </summary>
public static class EntraGroupsEndpoints
{
	/// <summary>Mapeia os endpoints de leitura do diretório federado.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapEntraGroupsEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/entra")
			.WithTags("Federation")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

		group.MapGet("/groups", async (
				Guid tenantId,
				string? filter,
				string? pageToken,
				int? pageSize,
				ListEntraGroupsHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, filter, pageToken, pageSize ?? 0, cancellationToken))
				.ToHttpResult(page => Results.Ok(page)))
			.WithName("ListEntraGroups")
			.WithSummary("Lista os grupos do diretório federado do tenant (Microsoft Entra ID), paginado por cursor.")
			.Produces<EntraGroupPage>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status409Conflict)
			.ProducesProblem(StatusCodes.Status503ServiceUnavailable);

		return endpoints;
	}
}
