namespace Secco.SecureGate.Api.Identity;

/// <summary>
/// Saneamento do <c>login_hint</c> que o authorize repassa ao Entra ID (login federado direto). O valor vem do
/// relying party e é input externo (ADR-0020): só passa o que tem forma de e-mail e cabe no limite, nunca é
/// registrado em log e nunca concede nada — é só uma dica de preenchimento.
/// </summary>
internal static class LoginHint
{
	/// <summary>Tamanho máximo aceito (o de um endereço de e-mail, RFC 5321).</summary>
	public const int MaxLength = 254;

	/// <summary>
	/// Devolve o valor se for um e-mail plausível (um único <c>@</c> com texto dos dois lados, sem espaço nem
	/// caractere de controle, até <see cref="MaxLength"/>); senão <c>null</c>.
	/// </summary>
	/// <param name="value">Valor do parâmetro <c>login_hint</c>.</param>
	public static string? Sanitize(string? value)
	{
		if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
		{
			return null;
		}

		foreach (var character in value)
		{
			if (char.IsControl(character) || char.IsWhiteSpace(character))
			{
				return null;
			}
		}

		var at = value.IndexOf('@', StringComparison.Ordinal);

		if (at <= 0 || at == value.Length - 1 || value.IndexOf('@', at + 1) >= 0)
		{
			return null;
		}

		return value;
	}
}
