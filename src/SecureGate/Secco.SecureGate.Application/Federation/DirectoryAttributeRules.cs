namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Regras dos atributos de pessoa vindos do diretório federado (input externo, ADR-0020). Texto acima do limite
/// ou com caractere de controle é descartado — nunca truncado, nunca persistido.
/// </summary>
public static class DirectoryAttributeRules
{
	public const int JobTitleMaxLength = 128;
	public const int DepartmentMaxLength = 128;
	public const int BusinessPhoneMaxLength = 64;

	/// <summary>
	/// Apara o texto; vazio vira nulo (sem descarte). Acima de <paramref name="maxLength"/> (medido depois do
	/// <c>Trim</c>) ou com qualquer caractere de controle devolve nulo com <paramref name="discarded"/> = true.
	/// </summary>
	public static string? Normalize(string? raw, int maxLength, out bool discarded)
	{
		discarded = false;
		if (raw is null)
		{
			return null;
		}

		// Controle é checado no texto cru: o Trim engoliria U+0085 (NEL) e quebras nas pontas.
		var text = raw.Trim();
		if (raw.Any(char.IsControl) || text.Length > maxLength)
		{
			discarded = true;
			return null;
		}

		return text.Length == 0 ? null : text;
	}
}
