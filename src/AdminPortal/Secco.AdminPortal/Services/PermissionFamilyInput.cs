using Secco.SharedKernel.Authorization;

namespace Secco.AdminPortal.Services;

/// <summary>
/// Composição e agrupamento de instâncias de família de permissão na edição de perfil (ADR-0043).
/// Validação só de formato, no cliente da tela — a recusa final continua sendo do SecureGate.
/// </summary>
public static class PermissionFamilyInput
{
	/// <summary>
	/// Compõe a permissão da instância a partir da família e do valor digitado, sem lançar exceção.
	/// </summary>
	/// <param name="family">Família declarada no catálogo (ex.: <c>setor-{id}:read</c>).</param>
	/// <param name="value">Valor digitado (kebab-case minúsculo; espaços nas pontas são ignorados).</param>
	/// <param name="permission">A permissão composta, quando válida.</param>
	/// <param name="error">Mensagem para o operador, quando inválida.</param>
	public static bool TryCompose(string family, string? value, out string permission, out string error)
	{
		permission = string.Empty;
		var trimmed = value?.Trim();

		if (string.IsNullOrEmpty(trimmed))
		{
			error = "Informe o valor da instância.";

			return false;
		}

		try
		{
			permission = SeccoPermissions.FromFamily(family, trimmed);
			error = string.Empty;

			return true;
		}
		catch (ArgumentException)
		{
			error = "Valor inválido: use minúsculas, números e hífens (ex.: rh ou recursos-humanos), sem hífen nas pontas.";

			return false;
		}
	}

	/// <summary>Das permissões informadas, as que são instância da família (comparação ordinal, sem regex).</summary>
	/// <param name="family">Família declarada no catálogo.</param>
	/// <param name="permissions">Permissões candidatas.</param>
	public static IReadOnlyList<string> InstancesOf(string family, IEnumerable<string> permissions) =>
		[.. permissions.Where(permission => SeccoPermissions.MatchesFamily(family, permission)).Order(StringComparer.Ordinal)];
}
