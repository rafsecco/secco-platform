using System.Security.Claims;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Elevation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Gestão da concessão de elevação (<c>/api/v1/tenants/{tenantId}/users/{userId}/elevation</c>,
/// ADR-0031): a AUTORIDADE explícita para um usuário de tenant de cliente trocar o próprio
/// token pelo token estreito de leitura de log cross-tenant (mecanismo implementado à parte,
/// no núcleo). Escopo <c>securegate:admin</c> — mesma policy de <see cref="UserEndpoints"/>.
/// </summary>
public static class ElevationGrantEndpoints
{
	/// <summary>Mapeia os endpoints de gestão de concessões de elevação.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapElevationGrantEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/users/{userId:guid}/elevation")
			.WithTags("Elevation")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

		group.MapPut("/", async (
				Guid tenantId,
				Guid userId,
				GrantElevationRequest request,
				ClaimsPrincipal caller,
				GrantElevationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new GrantElevationCommand(
					tenantId, userId, caller.FindFirstValue(SeccoClaims.Subject) ?? string.Empty, request.ExpiresAt),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("GrantElevation")
			.WithSummary("Concede ou renova a elevação de leitura de log cross-tenant do usuário (ADR-0031). GrantedBy vem do claim 'sub' do chamador, nunca do corpo.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/", async (
				Guid tenantId,
				Guid userId,
				GetElevationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.WithName("GetElevation")
			.WithSummary("Busca a concessão de elevação do usuário, com o estado ativo/expirado calculado.")
			.Produces<ElevationGrantDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/", async (
				Guid tenantId,
				Guid userId,
				RevokeElevationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RevokeElevation")
			.WithSummary("Revoga a concessão de elevação do usuário — idempotente (204 mesmo se não havia concessão).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}
}
