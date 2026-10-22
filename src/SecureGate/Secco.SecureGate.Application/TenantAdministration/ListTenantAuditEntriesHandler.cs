using Secco.SecureGate.Application.Tenants;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>
/// Lê a trilha de autoridade de um tenant (ADR-0054). Só o operador chega aqui: a rota exige
/// <c>securegate:admin</c> e não está aberta a admin de tenant nem à gestão por máquina — a trilha existe contra elas.
/// </summary>
public sealed class ListTenantAuditEntriesHandler(ITenantRepository tenants, IAuthorityAuditRepository entries)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="page">Página pedida (já limitada pelo SharedKernel).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<AuthorityAuditEntryDto>>> HandleAsync(
		Guid tenantId, PageRequest page, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(page);

		if (await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
		{
			return SecureGateErrors.Tenants.NotFound;
		}

		return await entries.ListByTenantAsync(tenantId, page, cancellationToken).ConfigureAwait(false);
	}
}
