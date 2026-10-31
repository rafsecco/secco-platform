namespace Secco.SecureGate.Domain.Tenants;

/// <summary>
/// Regras dos domínios de e-mail permitidos de uma federação (ADR-0055): a lista que o operador cadastra e que
/// decide se o e-mail lido do Graph pode virar conta. Nome de domínio é input externo (do operador, e depois
/// comparado com texto do diretório do cliente): formato estrito, sem curinga, com teto de quantidade e de tamanho.
/// </summary>
public static class EmailDomainRules
{
	/// <summary>Quantidade máxima de domínios por federação.</summary>
	public const int MaxCount = 10;

	/// <summary>Tamanho máximo de um domínio (limite do DNS).</summary>
	public const int MaxDomainLength = 253;

	private const int MaxLabelLength = 63;

	/// <summary>Separador do armazenamento (vírgula nunca aparece em hostname).</summary>
	private const char StorageSeparator = ',';

	/// <summary>Normaliza e valida um domínio: minúsculo, sem espaços, hostname DNS com ao menos dois rótulos.</summary>
	/// <param name="input">Valor cru.</param>
	/// <param name="domain">O domínio normalizado, quando válido.</param>
	/// <returns><c>false</c> se vazio, com curinga, longo demais ou fora do formato de hostname.</returns>
	public static bool TryNormalize(string? input, out string domain)
	{
		domain = string.Empty;
		var candidate = input?.Trim().ToLowerInvariant();

		if (string.IsNullOrEmpty(candidate) || candidate.Length > MaxDomainLength)
		{
			return false;
		}

		var labels = candidate.Split('.');

		if (labels.Length < 2)
		{
			return false;
		}

		foreach (var label in labels)
		{
			if (!IsValidLabel(label))
			{
				return false;
			}
		}

		// O rótulo final (TLD) nunca é só numérico: evita literal de IP disfarçado de domínio
		if (labels[^1].All(char.IsAsciiDigit))
		{
			return false;
		}

		domain = candidate;

		return true;
	}

	/// <summary>
	/// Normaliza a lista inteira: remove repetidos e ordena. Falha se qualquer item for inválido ou se passar do teto.
	/// A lista vazia é válida (provisionamento sem domínio nenhum recusa todo e-mail).
	/// </summary>
	/// <param name="input">Valores crus.</param>
	/// <param name="domains">Os domínios normalizados.</param>
	/// <returns><c>false</c> se algum item é inválido ou há mais que <see cref="MaxCount"/>.</returns>
	public static bool TryNormalizeAll(IEnumerable<string?> input, out IReadOnlyList<string> domains)
	{
		ArgumentNullException.ThrowIfNull(input);
		var result = new SortedSet<string>(StringComparer.Ordinal);
		domains = [];

		foreach (var item in input)
		{
			if (!TryNormalize(item, out var domain))
			{
				return false;
			}

			result.Add(domain);
		}

		if (result.Count > MaxCount)
		{
			return false;
		}

		domains = [.. result];

		return true;
	}

	/// <summary>Serializa a lista (já normalizada) para a coluna.</summary>
	/// <param name="domains">Domínios normalizados.</param>
	public static string Serialize(IEnumerable<string> domains) =>
		string.Join(StorageSeparator, domains);

	/// <summary>Lê a coluna de volta; item malformado (nunca deveria existir) é descartado, não adivinhado.</summary>
	/// <param name="stored">Valor persistido.</param>
	public static IReadOnlyList<string> Deserialize(string? stored) =>
		string.IsNullOrEmpty(stored)
			? []
			: [.. stored.Split(StorageSeparator, StringSplitOptions.RemoveEmptyEntries)
				.Select(item => TryNormalize(item, out var domain) ? domain : null)
				.OfType<string>()];

	/// <summary>Teto da coluna: o máximo de domínios no tamanho máximo, mais os separadores.</summary>
	public static int ColumnMaxLength => (MaxCount * MaxDomainLength) + (MaxCount - 1);

	private static bool IsValidLabel(string label)
	{
		if (label.Length is 0 or > MaxLabelLength || label[0] == '-' || label[^1] == '-')
		{
			return false;
		}

		foreach (var character in label)
		{
			if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
			{
				return false;
			}
		}

		return true;
	}
}
