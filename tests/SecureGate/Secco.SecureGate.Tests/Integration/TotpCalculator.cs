using System.Globalization;
using System.Security.Cryptography;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Calcula o dígito TOTP do lado do teste, como o aplicativo autenticador da pessoa faria.
/// </summary>
/// <remarks>
/// Existe porque o <c>AuthenticatorTokenProvider</c> do Identity <b>não gera</b> código: por
/// design, <c>GenerateTwoFactorTokenAsync</c> devolve string vazia para esse provedor — o dígito
/// nasce no aplicativo do usuário, e o servidor só valida. O algoritmo aqui é o mesmo que a
/// validação do Identity usa (RFC 6238, HMAC-SHA1, passo de 30 s, 6 dígitos).
/// </remarks>
internal static class TotpCalculator
{
	/// <summary>
	/// Calcula o dígito TOTP a partir da chave base32, exatamente como o aplicativo autenticador
	/// da pessoa faria (RFC 6238, o mesmo algoritmo que o <c>AuthenticatorTokenProvider</c> do
	/// Identity usa para VALIDAR). O provider do Identity não serve para gerar: por design,
	/// <c>GenerateTwoFactorTokenAsync</c> devolve string vazia para o provedor "Authenticator" —
	/// o código nasce no aplicativo da pessoa, nunca no servidor.
	/// </summary>
	public static string Compute(string base32Key)
	{
		var keyBytes = Base32Decode(base32Key);
		var timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;

		var timestepBytes = BitConverter.GetBytes(timestep);
		if (BitConverter.IsLittleEndian)
		{
			Array.Reverse(timestepBytes);
		}

		using var hmac = new HMACSHA1(keyBytes);
		var hash = hmac.ComputeHash(timestepBytes);

		var offset = hash[^1] & 0xf;
		var binaryCode = ((hash[offset] & 0x7f) << 24)
			| ((hash[offset + 1] & 0xff) << 16)
			| ((hash[offset + 2] & 0xff) << 8)
			| (hash[offset + 3] & 0xff);

		return (binaryCode % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
	}

	/// <summary>Decodifica base32 (RFC 4648) sem padding — o formato da chave do autenticador.</summary>
	private static byte[] Base32Decode(string input)
	{
		const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
		var normalized = input.TrimEnd('=').ToUpperInvariant();

		var bits = 0;
		var value = 0;
		var output = new List<byte>();

		foreach (var c in normalized)
		{
			value = (value << 5) | alphabet.IndexOf(c);
			bits += 5;

			if (bits >= 8)
			{
				output.Add((byte)((value >> (bits - 8)) & 0xff));
				bits -= 8;
			}
		}

		return [.. output];
	}
}
