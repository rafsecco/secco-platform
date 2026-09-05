using System.Security.Cryptography;

namespace Secco.SecureGate.Application.Provisioning;

/// <summary>
/// Geração da senha do usuário de banco criado no provisionamento.
/// </summary>
/// <remarks>
/// Duas restrições governam o alfabeto, e as duas são práticas, não estéticas. A primeira: os
/// caracteres <c>;</c>, <c>=</c>, <c>'</c> e <c>"</c> ficam fora porque são justamente os
/// delimitadores de connection string e de literal SQL — uma senha que os contenha transforma
/// cada ponto de montagem num risco de quebra ou de injeção. A segunda: o alfabeto é um número
/// de caracteres que não divide 256 igualmente, então a amostragem descarta bytes fora da faixa
/// utilizável em vez de usar módulo, que introduziria viés estatístico.
/// </remarks>
public static class TenantDatabaseSecretGenerator
{
	/// <summary>Comprimento da senha gerada.</summary>
	public const int PasswordLength = 40;

	/// <summary>Alfabeto seguro para connection string e literal SQL.</summary>
	private const string Alphabet =
		"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.~!*+@#%^";

	/// <summary>Gera uma senha aleatória criptográfica.</summary>
	public static string GeneratePassword()
	{
		var password = new char[PasswordLength];
		var buffer = new byte[1];

		// Maior múltiplo do alfabeto que cabe em um byte: acima disso, o byte é descartado.
		var usableCeiling = (byte)(256 / Alphabet.Length * Alphabet.Length);

		for (var index = 0; index < password.Length;)
		{
			RandomNumberGenerator.Fill(buffer);

			if (buffer[0] >= usableCeiling)
			{
				continue;
			}

			password[index++] = Alphabet[buffer[0] % Alphabet.Length];
		}

		return new string(password);
	}
}
