using System.Security.Claims;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Tenancy;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Application.Tenants;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Admin de tenant (ADR-0044): a gestão das concessões (<c>/api/v1/tenants/{tenantId}/admins</c>), só do
/// operador de instalação, e a leitura dos tenants que o chamador administra
/// (<c>/api/v1/tenant-admin/tenants</c>), para a aplicação montar a área administrativa.
/// </summary>
public static class TenantAdminEndpoints
{
	/// <summary>Mapeia os endpoints de admin de tenant.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapTenantAdminEndpoints(this IEndpointRouteBuilder endpoints)
	{
		// Concessão é só do operador: admin de tenant não nomeia outro admin nem a si mesmo
		var grants = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/admins")
			.WithTags("TenantAdmins")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)))
			.RequireTenantScope();

		grants.MapGet("/", async (Guid tenantId, ListTenantAdminsHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, cancellationToken))
				.ToHttpResult(admins => Results.Ok(admins)))
			.WithName("ListTenantAdmins")
			.WithSummary("Lista as pessoas com concessão de admin deste tenant (ADR-0044).")
			.Produces<IReadOnlyList<TenantAdminDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		grants.MapPut("/{userId:guid}", async (
				Guid tenantId,
				Guid userId,
				ClaimsPrincipal caller,
				GrantTenantAdminHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new GrantTenantAdminCommand(tenantId, userId, caller.FindFirstValue(SeccoClaims.Subject) ?? string.Empty),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("GrantTenantAdmin")
			.WithSummary("Concede a autoridade de admin deste tenant à pessoa — idempotente. A pessoa pode ser de qualquer tenant; o tenant de plataforma responde 409.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		grants.MapDelete("/{userId:guid}", async (
				Guid tenantId,
				Guid userId,
				RevokeTenantAdminHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RevokeTenantAdmin")
			.WithSummary("Revoga a autoridade de admin deste tenant — idempotente; vale na próxima requisição da pessoa.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		endpoints.MapGet("/api/v1/tenant-admin/tenants", async (
				ClaimsPrincipal caller,
				ITenantAdminAuditor auditor,
				ListAdministeredTenantsHandler handler,
				CancellationToken cancellationToken) =>
			!auditor.IsConfigured || !Guid.TryParse(caller.FindFirstValue(SeccoClaims.Subject), out var userId)
				? TenantAdminAuthority.Forbidden()
				: (await handler.HandleAsync(userId, cancellationToken)).ToHttpResult(tenants => Results.Ok(tenants)))
			.WithTags("TenantAdmins")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.TenantAdmin)))
			.WithName("ListAdministeredTenants")
			.WithSummary("Lista os tenants que a pessoa logada administra (ADR-0044).")
			.Produces<IReadOnlyList<TenantDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status403Forbidden);

		return endpoints;
	}
}
