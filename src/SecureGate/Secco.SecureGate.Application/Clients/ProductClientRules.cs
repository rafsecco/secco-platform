using System.Security.Cryptography;

namespace Secco.SecureGate.Application.Clients;

/// <summary>Regras de formato e geração de credencial do client de produto (ADR-0037, ADR-0020).</summary>
public static class ProductClientRules
{
	/// <summary>Tamanho máximo do nome.</summary>
	public const int NameMaxLength = 100;

	/// <summary>Máximo de escopos por client.</summary>
	public const int MaxScopes = 10;

	/// <summary>Máximo de papéis por client.</summary>
	public const int MaxRoles = 20;

	/// <summary>
	/// Prefixo de todo <c>client_id</c> gerado pela API. A configuração de plataforma é proibida de
	/// usá-lo, então os dois espaços de nome nunca se cruzam.
	/// </summary>
	public const string ClientIdPrefix = "cli_";

	private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

	/// <summary>Gera um <c>client_id</c> aleatório — o chamador nunca escolhe (sem colisão nem imitação).</summary>
	public static string NewClientId()
	{
		Span<char> suffix = stackalloc char[16];

		for (var i = 0; i < suffix.Length; i++)
		{
			suffix[i] = Base32Alphabet[RandomNumberGenerator.GetInt32(Base32Alphabet.Length)];
		}

		return ClientIdPrefix + new string(suffix);
	}

	/// <summary>Gera um secret de 32 bytes aleatórios em base64url, exibido uma única vez.</summary>
	public static string NewSecret() =>
		Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			.TrimEnd('=').Replace('+', '-').Replace('/', '_');

	/// <summary>Valida um nome já aparado: 1–100 caracteres, sem caractere de controle.</summary>
	/// <param name="name">Nome candidato.</param>
	public static bool IsValidName(string name) =>
		name.Length is > 0 and <= NameMaxLength && !name.Any(char.IsControl);
}
