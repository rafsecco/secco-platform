using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Tenancy;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Pagination;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Leitura da trilha de autoridade guardada no SecureGate (ADR-0054): <c>/api/v1/tenants/{tenantId}/audit-entries</c>.
/// Só o operador (<c>securegate:admin</c>) lê. A rota NÃO é marcada para admin de tenant nem para a gestão por
/// máquina: a trilha existe contra elas, e o client com <c>tenant-manage</c> não lê a própria trilha.
/// </summary>
public static class AuthorityAuditEndpoints
{
	/// <summary>Mapeia a leitura da trilha de autoridade.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapAuthorityAuditEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/audit-entries")
			.WithTags("AuthorityAudit")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)))
			.RequireTenantScope();

		group.MapGet("/", async (
				Guid tenantId,
				int? page,
				int? size,
				ListTenantAuditEntriesHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				tenantId,
				new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize),
				cancellationToken))
				.ToHttpResult(result => Results.Ok(result)))
			.WithName("ListTenantAuditEntries")
			.WithSummary("Lista, paginadas e do mais recente para o mais antigo, as entradas da trilha de autoridade do tenant (ADR-0054). Só o operador lê.")
			.Produces<PagedResult<AuthorityAuditEntryDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}
}
