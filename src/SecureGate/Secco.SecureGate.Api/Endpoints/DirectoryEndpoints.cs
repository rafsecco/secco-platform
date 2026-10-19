using System.Security.Claims;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Directory;
using Secco.SecureGate.Application.Tenancy;
using Secco.SharedKernel.Constants;
using Secco.SharedKernel.Pagination;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Diretório de pessoas do tenant para client de produto (<c>/api/v1/directory</c>, ADR-0045). Exige o
/// scope <c>directory:read</c> E uma claim <c>tenant_id</c> válida; o tenant vem SÓ dela — rota, query
/// e header não participam, então o client não consegue olhar para fora do próprio tenant.
/// </summary>
public static class DirectoryEndpoints
{
	/// <summary>Mapeia os endpoints do diretório.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapDirectoryEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/directory")
			.WithTags("Directory")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context =>
					ScopeAuthorization.HasScope(context.User, SecureGateScopes.DirectoryRead)
					&& TryGetTenant(context.User, out _)));

		group.MapGet("/people", async (
				int? page,
				int? size,
				ClaimsPrincipal caller,
				ITenantAccessScope accessScope,
				ListDirectoryPeopleHandler handler,
				CancellationToken cancellationToken) =>
			{
				if (!TryGetTenant(caller, out var tenantId))
				{
					return Results.Forbid();
				}

				using (accessScope.EnterTenant(tenantId))
				{
					return (await handler.HandleAsync(tenantId, page, size, cancellationToken))
						.ToHttpResult(result => Results.Ok(result));
				}
			})
			.WithName("ListDirectoryPeople")
			.WithSummary("Lista, paginadas (até 100) e por e-mail, as pessoas do tenant do token: donos da conta e membros com vínculo ativo.")
			.Produces<PagedResult<DirectoryPersonDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status403Forbidden);

		group.MapGet("/people/{userId:guid}", async (
				Guid userId,
				ClaimsPrincipal caller,
				ITenantAccessScope accessScope,
				GetDirectoryPersonHandler handler,
				CancellationToken cancellationToken) =>
			{
				if (!TryGetTenant(caller, out var tenantId))
				{
					return Results.Forbid();
				}

				using (accessScope.EnterTenant(tenantId))
				{
					return (await handler.HandleAsync(tenantId, userId, cancellationToken))
						.ToHttpResult(dto => Results.Ok(dto));
				}
			})
			.WithName("GetDirectoryPerson")
			.WithSummary("Detalha uma pessoa do tenant do token; pessoa de outro tenant responde 404, igual à inexistente.")
			.Produces<DirectoryPersonDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status403Forbidden)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}

	private static bool TryGetTenant(ClaimsPrincipal user, out Guid tenantId) =>
		Guid.TryParse(user.FindFirst(SeccoClaims.TenantId)?.Value, out tenantId) && tenantId != Guid.Empty;
}
