using Secco.SecureGate.Application;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Quando o escopo <c>securegate:tenant-admin</c> sai num token de pessoa (ADR-0044). Um lugar só para o
/// login e a renovação — a mesma lição do filtro de operador: sem a renovação, quem perdeu a concessão
/// seguiria renovando o escopo.
/// </summary>
internal static class TenantAdminScopePolicy
{
	/// <summary>
	/// Indica se a conta precisa cadastrar o segundo fator antes de receber o code: pediu o escopo, tem
	/// concessão e a autoridade está ligada, mas não tem segundo fator (como o operador, entrega D).
	/// </summary>
	/// <param name="user">Conta autenticada pelo cookie.</param>
	/// <param name="requestedScopes">Escopos pedidos no authorize.</param>
	/// <param name="grants">Concessões.</param>
	/// <param name="auditor">Auditor (a autoridade só existe com ele configurado).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<bool> RequiresEnrollmentAsync(
		User user,
		IEnumerable<string> requestedScopes,
		ITenantAdminGrantRepository grants,
		ITenantAdminAuditor auditor,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(user);

		return !user.TwoFactorEnabled
			&& auditor.IsConfigured
			&& requestedScopes.Contains(SecureGateScopes.TenantAdmin, StringComparer.Ordinal)
			&& await grants.HasAnyAuthorityAsync(user.Id, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Tira o escopo de quem não pode recebê-lo AGORA: sem concessão nenhuma (de admin ou de criação, ADR-0047),
	/// sem segundo fator, ou com a
	/// autoridade desligada (sem identidade de auditoria). Os demais escopos passam intactos.
	/// </summary>
	/// <param name="user">Conta.</param>
	/// <param name="scopes">Escopos já filtrados pela regra do operador.</param>
	/// <param name="grants">Concessões.</param>
	/// <param name="auditor">Auditor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<IReadOnlyList<string>> FilterScopesAsync(
		User user,
		IReadOnlyList<string> scopes,
		ITenantAdminGrantRepository grants,
		ITenantAdminAuditor auditor,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(user);
		ArgumentNullException.ThrowIfNull(scopes);

		if (!scopes.Contains(SecureGateScopes.TenantAdmin, StringComparer.Ordinal))
		{
			return scopes;
		}

		var keep = user.TwoFactorEnabled
			&& auditor.IsConfigured
			&& await grants.HasAnyAuthorityAsync(user.Id, cancellationToken).ConfigureAwait(false);

		return keep ? scopes : [.. scopes.Where(scope => scope != SecureGateScopes.TenantAdmin)];
	}
}
