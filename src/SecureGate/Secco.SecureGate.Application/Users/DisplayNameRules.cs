namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Regras de formato para o nome de exibição do usuário (#30). Diferente de <c>RoleInputRules</c>,
/// não restringe a um alfabeto — nome de pessoa carrega acento, espaço, hífen e apóstrofo —, só
/// exclui o que é perigoso ou sem sentido num campo de identidade.
/// </summary>
/// <remarks>
/// O valor viaja para a claim <c>name</c> do token e para a trilha de auditoria (texto livre em
/// ambos): caractere de controle (inclusive CR/LF) é a única coisa proibida por ADR-0020 — log
/// forging e quebra de linha num header ou numa linha de log são o risco concreto, não XSS (Razor
/// já codifica a saída, e o token não é HTML).
/// </remarks>
public static class DisplayNameRules
{
	/// <summary>Tamanho máximo aceito, e também o teto da coluna no banco.</summary>
	public const int MaxLength = 160;

	/// <summary>
	/// Normaliza a entrada: apara espaços e converte vazio/só-espaço em <c>null</c> (limpar o
	/// nome é uma operação válida, não um erro de formato).
	/// </summary>
	/// <param name="input">Valor cru, possivelmente <c>null</c>.</param>
	public static string? Normalize(string? input)
	{
		var trimmed = input?.Trim();

		return string.IsNullOrEmpty(trimmed) ? null : trimmed;
	}

	/// <summary>Valida um nome já normalizado (não nulo, aparado). Vazio nunca chega aqui — vira <c>null</c> antes.</summary>
	/// <param name="name">Nome candidato, já normalizado.</param>
	public static bool IsValid(string name) =>
		!string.IsNullOrEmpty(name) && name.Length <= MaxLength && !ContainsControlCharacter(name);

	private static bool ContainsControlCharacter(string value)
	{
		foreach (var character in value)
		{
			if (char.IsControl(character))
			{
				return true;
			}
		}

		return false;
	}
}
