using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Leitura dos grupos do diretório federado de um tenant (issue #27) e o mapeamento explícito de
/// grupo para perfil (issue #28), ADR-0036. Nesta rodada, o mapeamento é só CRUD — a reconciliação
/// automática (login e job periódico) fica para uma entrega seguinte.
/// </summary>
public static class EntraGroupsEndpoints
{
	/// <summary>Mapeia os endpoints de leitura do diretório federado e de mapeamento grupo→perfil.</summary>
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

		group.MapPost("/group-role-mappings", async (
				Guid tenantId,
				CreateGroupRoleMappingRequest request,
				CreateGroupRoleMappingHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new CreateGroupRoleMappingCommand(
					tenantId, request.EntraGroupId ?? Guid.Empty, request.EntraGroupDisplayName, request.RoleName),
				cancellationToken))
				.ToHttpResult(mapping => Results.Created(
					$"/api/v1/tenants/{tenantId}/entra/group-role-mappings/{mapping.Id}", mapping)))
			.WithName("CreateGroupRoleMapping")
			.WithSummary("Mapeia um grupo do diretório federado para um perfil do tenant (issue #28). Exige federação habilitada.")
			.Produces<GroupRoleMappingRecord>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapGet("/group-role-mappings", async (
				Guid tenantId,
				ListGroupRoleMappingsHandler handler,
				CancellationToken cancellationToken) =>
			Results.Ok(await handler.HandleAsync(tenantId, cancellationToken)))
			.WithName("ListGroupRoleMappings")
			.WithSummary("Lista os mapeamentos grupo→perfil do tenant.")
			.Produces<IReadOnlyList<GroupRoleMappingRecord>>(StatusCodes.Status200OK);

		group.MapDelete("/group-role-mappings/{mappingId:guid}", async (
				Guid tenantId,
				Guid mappingId,
				DeleteGroupRoleMappingHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, mappingId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("DeleteGroupRoleMapping")
			.WithSummary("Remove um mapeamento grupo→perfil. Não retira o perfil de quem já o tem por causa dele.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}
}
