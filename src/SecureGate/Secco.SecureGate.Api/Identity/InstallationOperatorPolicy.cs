using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Quem é operador de instalação e o que isso concede no token (ADR-0023/0024). Um lugar só, usado no
/// login, na renovação e na montagem do principal — era a ausência dele na renovação que deixava um
/// operador rebaixado renovando <c>securegate:admin</c>.
/// </summary>
internal static class InstallationOperatorPolicy
{
	/// <summary>
	/// Operador é quem tem o perfil de operador, é do tenant de plataforma E está logando nele. O nome do
	/// perfil é único só por tenant: exigir o tenant impede que um homônimo num tenant de cliente escale —
	/// inclusive o operador que é membro de outro tenant onde existe um perfil homônimo (ADR-0040).
	/// </summary>
	/// <param name="user">Conta.</param>
	/// <param name="roles">Perfis da conta no tenant do login.</param>
	/// <param name="loginTenantId">Tenant do login (sem valor padrão: todo chamador decide).</param>
	public static bool IsInstallationOperator(User user, IEnumerable<string> roles, Guid loginTenantId)
	{
		ArgumentNullException.ThrowIfNull(user);

		return loginTenantId == SecureGatePlatform.TenantId
			&& user.TenantId == SecureGatePlatform.TenantId
			&& roles.Contains(SecureGatePlatform.OperatorRole, StringComparer.Ordinal);
	}

	/// <summary>Remove <c>securegate:admin</c> dos scopes de quem não é operador.</summary>
	/// <param name="user">Conta.</param>
	/// <param name="roles">Perfis da conta no tenant do login.</param>
	/// <param name="scopes">Scopes pedidos (login) ou herdados (renovação).</param>
	/// <param name="loginTenantId">Tenant do login.</param>
	public static IReadOnlyList<string> FilterScopes(
		User user, IEnumerable<string> roles, IEnumerable<string> scopes, Guid loginTenantId)
	{
		var isOperator = IsInstallationOperator(user, roles, loginTenantId);

		return [.. scopes.Where(scope => isOperator || scope != SecureGateScopes.Admin)];
	}
}
