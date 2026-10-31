namespace Secco.AdminPortal.Services;

/// <summary>
/// Lê o campo de domínios de e-mail permitidos da federação (ADR-0055). A tela só separa os itens; a validação do
/// formato (hostname, sem curinga, no máximo 10) é do SecureGate, que responde 400 com a mensagem.
/// </summary>
public static class FederationDomainsInput
{
	/// <summary>Texto que, sozinho, esvazia a lista (em branco mantém a atual).</summary>
	public const string ClearMarker = "-";

	private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

	/// <summary>
	/// Em branco = <c>null</c> (o SecureGate mantém a lista atual); <c>-</c> = lista vazia (esvazia); senão os itens
	/// separados por vírgula, ponto e vírgula ou espaço, sem repetição.
	/// </summary>
	/// <param name="input">Texto digitado.</param>
	public static IReadOnlyList<string>? Parse(string? input)
	{
		var trimmed = input?.Trim();

		if (string.IsNullOrEmpty(trimmed))
		{
			return null;
		}

		if (trimmed == ClearMarker)
		{
			return [];
		}

		return
		[
			.. trimmed.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
				.Distinct(StringComparer.OrdinalIgnoreCase)
		];
	}
}
