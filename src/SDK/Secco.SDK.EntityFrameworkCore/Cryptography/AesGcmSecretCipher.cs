using System.Security.Cryptography;
using System.Text;

namespace Secco.SDK.EntityFrameworkCore.Cryptography;

/// <summary>
/// Cifragem AES-256-GCM de segredo em repouso (ADR-0025).
/// </summary>
/// <remarks>
/// Formato de armazenamento <c>secco-enc:v1:&lt;base64(nonce ‖ ciphertext ‖ tag)&gt;</c> —
/// versionado e autodescritivo, sem dependência externa (<see cref="AesGcm"/> é BCL). GCM é
/// AEAD: o tag autentica o dado, então adulteração falha em vez de decifrar lixo.
/// <para>
/// O tipo recebe as chaves <b>já decodificadas e validadas</b> e não conhece configuração de
/// produto nenhum. É o que permite ao SecureGate manter a política dele (chave de DEV embutida,
/// fail-fast em Production) sem que essa política vaze para o SDK e valha para todo mundo.
/// </para>
/// </remarks>
public sealed class AesGcmSecretCipher : ISeccoSecretCipher
{
	/// <summary>Prefixo que marca um valor cifrado — o que não o tem é legado em claro.</summary>
	public const string Prefix = "secco-enc:";

	/// <summary>Prefixo completo da versão 1 do formato.</summary>
	public const string VersionOnePrefix = $"{Prefix}v1:";

	/// <summary>Tamanho exigido da chave: 32 bytes (AES-256).</summary>
	public const int KeySizeInBytes = 32;

	private const int NonceSizeInBytes = 12;  // 96 bits — recomendado para AES-GCM
	private const int TagSizeInBytes = 16;    // 128 bits — máximo do GCM

	private readonly byte[] _activeKey;
	private readonly IReadOnlyList<byte[]> _retiredKeys;

	/// <summary>Cria o cifrador com a chave ativa e, opcionalmente, as aposentadas.</summary>
	/// <param name="activeKey">Chave que cifra e decifra. 32 bytes.</param>
	/// <param name="retiredKeys">Chaves que apenas decifram, durante a rotação.</param>
	/// <exception cref="SeccoSecretCipherException">Se alguma chave não tiver 32 bytes.</exception>
	public AesGcmSecretCipher(byte[] activeKey, IReadOnlyList<byte[]>? retiredKeys = null)
	{
		ArgumentNullException.ThrowIfNull(activeKey);

		_activeKey = Validate(activeKey);
		_retiredKeys = [.. (retiredKeys ?? []).Select(Validate)];
	}

	/// <summary>Cria o cifrador a partir de chaves em base64.</summary>
	/// <param name="activeKeyBase64">Chave ativa, base64 de 32 bytes.</param>
	/// <param name="retiredKeysBase64">Chaves aposentadas, base64 de 32 bytes.</param>
	/// <exception cref="SeccoSecretCipherException">Base64 inválido ou tamanho errado.</exception>
	public static AesGcmSecretCipher FromBase64Keys(
		string activeKeyBase64,
		IEnumerable<string>? retiredKeysBase64 = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyBase64);

		return new AesGcmSecretCipher(
			DecodeKey(activeKeyBase64),
			[.. (retiredKeysBase64 ?? []).Select(DecodeKey)]);
	}

	/// <inheritdoc />
	public string Encrypt(string plaintext)
	{
		ArgumentNullException.ThrowIfNull(plaintext);

		var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
		var nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
		var ciphertext = new byte[plaintextBytes.Length];
		var tag = new byte[TagSizeInBytes];

		using (var aes = new AesGcm(_activeKey, TagSizeInBytes))
		{
			aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
		}

		var blob = new byte[NonceSizeInBytes + ciphertext.Length + TagSizeInBytes];
		nonce.CopyTo(blob, 0);
		ciphertext.CopyTo(blob, NonceSizeInBytes);
		tag.CopyTo(blob, NonceSizeInBytes + ciphertext.Length);

		return VersionOnePrefix + Convert.ToBase64String(blob);
	}

	/// <inheritdoc />
	public string Decrypt(string stored)
	{
		ArgumentNullException.ThrowIfNull(stored);

		// Legado em claro: aceito na leitura, convergido por seeder no startup (ADR-0025)
		if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return stored;
		}

		var blob = ParseVersionOneBlob(stored);

		if (TryDecrypt(_activeKey, blob, out var plaintext))
		{
			return plaintext;
		}

		foreach (var retiredKey in _retiredKeys)
		{
			if (TryDecrypt(retiredKey, blob, out plaintext))
			{
				return plaintext;
			}
		}

		// Nenhuma chave serviu — dado adulterado ou chave desconhecida
		throw new SeccoSecretCipherException(
			"Não foi possível decifrar o segredo: dado adulterado ou chave desconhecida (ADR-0025).");
	}

	/// <inheritdoc />
	public bool IsEncryptedWithActiveKey(string stored)
	{
		ArgumentNullException.ThrowIfNull(stored);

		if (!stored.StartsWith(VersionOnePrefix, StringComparison.Ordinal))
		{
			// Inclui legado em claro e versão desconhecida. Versão desconhecida não é
			// "cifrado com a chave ativa": a convergência chamará Decrypt, que falha alto.
			return false;
		}

		return TryParseVersionOneBlob(stored, out var blob) && TryDecrypt(_activeKey, blob, out _);
	}

	private static byte[] Validate(byte[] key) =>
		key.Length == KeySizeInBytes
			? key
			: throw new SeccoSecretCipherException(
				$"Chave de cifragem deve ter {KeySizeInBytes} bytes (AES-256) (ADR-0025).");

	private static byte[] DecodeKey(string base64Key)
	{
		try
		{
			return Convert.FromBase64String(base64Key);
		}
		catch (FormatException exception)
		{
			throw new SeccoSecretCipherException("Chave de cifragem em base64 inválido (ADR-0025).", exception);
		}
	}

	private static byte[] ParseVersionOneBlob(string stored) =>
		TryParseVersionOneBlob(stored, out var blob)
			? blob
			: throw new SeccoSecretCipherException(
				"Versão de formato de cifragem desconhecida ou base64 inválido (ADR-0025).");

	private static bool TryParseVersionOneBlob(string stored, out byte[] blob)
	{
		blob = [];

		if (!stored.StartsWith(VersionOnePrefix, StringComparison.Ordinal))
		{
			return false;
		}

		try
		{
			var decoded = Convert.FromBase64String(stored[VersionOnePrefix.Length..]);

			if (decoded.Length < NonceSizeInBytes + TagSizeInBytes)
			{
				return false;
			}

			blob = decoded;
			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private static bool TryDecrypt(byte[] key, byte[] blob, out string plaintext)
	{
		plaintext = string.Empty;

		var nonce = blob.AsSpan(0, NonceSizeInBytes);
		var tag = blob.AsSpan(blob.Length - TagSizeInBytes, TagSizeInBytes);
		var ciphertext = blob.AsSpan(NonceSizeInBytes, blob.Length - NonceSizeInBytes - TagSizeInBytes);
		var plaintextBytes = new byte[ciphertext.Length];

		try
		{
			using var aes = new AesGcm(key, TagSizeInBytes);
			aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
			plaintext = Encoding.UTF8.GetString(plaintextBytes);
			return true;
		}
		catch (CryptographicException)
		{
			// Tag não confere para esta chave: adulterado OU chave errada — o chamador tenta a próxima
			return false;
		}
	}
}
