using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Exigência de segundo fator para o operador de instalação (entrega D, ADR-0030).
/// </summary>
/// <remarks>
/// O operador lê log de <b>todos</b> os tenants por elevação (ADR-0031) e gere a identidade da
/// instalação inteira: é a conta que compensa atacar, e a única em que o 2FA não é escolha.
/// <para>
/// A regra é aplicada no <c>/connect/authorize</c>, <b>antes</b> de qualquer código de autorização
/// sair — por isso nenhum relying party precisa saber dela, o AdminPortal inclusive (ADR-0023).
/// </para>
/// </remarks>
public static class OperatorTwoFactorPolicy
{
	/// <summary>Caminho do cadastro, para onde o operador sem segundo fator é enviado.</summary>
	public const string EnrollmentPath = "/conta/dois-fatores";

	/// <summary>Indica se a conta precisa cadastrar o segundo fator antes de receber token.</summary>
	/// <param name="user">Conta autenticada pelo cookie.</param>
	/// <param name="roles">Papéis da conta no tenant dela.</param>
	/// <param name="twoFactorEnabled">Estado atual do segundo fator.</param>
	public static bool RequiresEnrollment(User user, IEnumerable<string> roles, bool twoFactorEnabled) =>
		InstallationOperatorPolicy.IsInstallationOperator(user, roles) && !twoFactorEnabled;

	/// <summary>
	/// Operador não desliga o próprio segundo fator. Mesma lógica de ninguém desativar a própria
	/// conta nem se remover do papel de operador (issue #26): senão a exigência vira decoração.
	/// </summary>
	/// <param name="user">Conta autenticada pelo cookie.</param>
	/// <param name="roles">Papéis da conta no tenant dela.</param>
	public static bool CanDisable(User user, IEnumerable<string> roles) =>
		!InstallationOperatorPolicy.IsInstallationOperator(user, roles);
}
