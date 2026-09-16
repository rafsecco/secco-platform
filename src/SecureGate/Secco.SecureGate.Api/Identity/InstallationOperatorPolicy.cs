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
	/// Operador é quem tem o perfil de operador E está no tenant de plataforma. O nome do perfil é único
	/// só por tenant: exigir o tenant impede que um homônimo num tenant de cliente escale.
	/// </summary>
	/// <param name="user">Conta.</param>
	/// <param name="roles">Perfis atuais da conta.</param>
	public static bool IsInstallationOperator(User user, IEnumerable<string> roles)
	{
		ArgumentNullException.ThrowIfNull(user);

		return user.TenantId == SecureGatePlatform.TenantId
			&& roles.Contains(SecureGatePlatform.OperatorRole, StringComparer.Ordinal);
	}

	/// <summary>Remove <c>securegate:admin</c> dos scopes de quem não é operador.</summary>
	/// <param name="user">Conta.</param>
	/// <param name="roles">Perfis atuais da conta.</param>
	/// <param name="scopes">Scopes pedidos (login) ou herdados (renovação).</param>
	public static IReadOnlyList<string> FilterScopes(User user, IEnumerable<string> roles, IEnumerable<string> scopes)
	{
		var isOperator = IsInstallationOperator(user, roles);

		return [.. scopes.Where(scope => isOperator || scope != SecureGateScopes.Admin)];
	}
}
