using System.Security.Claims;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Members;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Vínculos pessoa-tenant (<c>/api/v1/tenants/{tenantId}/members</c>, ADR-0040). Escopo
/// <c>securegate:admin</c>. O vínculo diz se a pessoa PODE receber token para o tenant; o que ela pode
/// fazer lá continua sendo dos perfis do tenant (ADR-0021).
/// </summary>
public static class MembershipEndpoints
{
	/// <summary>Mapeia os endpoints de gestão de vínculos.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/members")
			.WithTags("Members")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

		group.MapPost("/", async (
				Guid tenantId,
				AddTenantMemberRequest request,
				AddTenantMemberHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, request.Email, cancellationToken))
				.ToHttpResult(dto => Results.Created($"/api/v1/tenants/{tenantId}/users/{dto.UserId}", dto)))
			.WithName("AddTenantMember")
			.WithSummary("Inclui no tenant uma pessoa que já tem conta, pelo e-mail (ADR-0040). Duplicata responde 409.")
			.Produces<TenantMemberDto>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapPut("/{userId:guid}", async (
				Guid tenantId,
				Guid userId,
				SetTenantMemberHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("SetTenantMember")
			.WithSummary("Inclui a pessoa no tenant ou reativa o vínculo suspenso (idempotente).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapPost("/{userId:guid}/suspend", async (
				Guid tenantId,
				Guid userId,
				ClaimsPrincipal caller,
				SuspendTenantMemberHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new SuspendTenantMemberCommand(tenantId, userId, caller.FindFirst(SeccoClaims.Subject)?.Value),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("SuspendTenantMember")
			.WithSummary("Suspende o vínculo (idempotente): a pessoa deixa de receber token para o tenant e as sessões dela são encerradas.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapDelete("/{userId:guid}", async (
				Guid tenantId,
				Guid userId,
				ClaimsPrincipal caller,
				RemoveTenantMemberHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new RemoveTenantMemberCommand(tenantId, userId, caller.FindFirst(SeccoClaims.Subject)?.Value),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RemoveTenantMember")
			.WithSummary("Remove o vínculo e os perfis da pessoa NESTE tenant (idempotente) e encerra as sessões dela.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		return endpoints;
	}
}
