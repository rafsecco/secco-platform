using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Secco.SDK.EntityFrameworkCore.Cryptography;

/// <summary>
/// Cifragem em envelope de arquivo, em streaming, com AES-256-GCM em blocos (ADR-0061).
/// </summary>
/// <remarks>
/// <para><b>Formato <c>SECCOENC</c> versão 1</b> — o conteúdo cifrado é:</para>
/// <code>
/// cabeçalho (9 bytes) = "SECCOENC" (ASCII) ‖ 0x01
/// bloco i             = AES-256-GCM(chave do arquivo, nonce_i, aad = cabeçalho, claro_i) ‖ tag (16 bytes)
/// nonce_i (12 bytes)  = 0x00 0x00 0x00 ‖ i (uint64 big-endian) ‖ (0x01 se último bloco, senão 0x00)
/// </code>
/// <para>
/// Todo bloco tem 64 KiB de conteúdo em claro, menos o último, que tem de 0 a 64 KiB. Só o arquivo
/// vazio tem bloco de 0 byte. Não há campo de tamanho nem de marca no arquivo: o último bloco é o
/// que termina o fluxo, e a marca de último entra no nonce.
/// </para>
/// <para>
/// O índice no nonce faz reordenar ou repetir blocos falhar. A marca de último faz o corte no fim
/// falhar. O cabeçalho como dado associado faz trocar a versão falhar. O nonce determinístico só é
/// seguro porque a chave do arquivo é sorteada aqui dentro a cada cifragem e nunca vem de fora.
/// Nenhuma chave cifra dois conteúdos. É a construção STREAM (Hoang, Reyhanitabar, Rogaway e
/// Vizár, 2015), a mesma do <c>age</c> e do Tink.
/// </para>
/// <para>
/// A chave do arquivo (32 bytes) é embrulhada pelo <see cref="ISeccoSecretCipher"/> recebido, sobre
/// o base64 dela. Sai no formato <c>secco-enc:v1:</c>. Valor sem esse prefixo é recusado: o
/// passthrough de legado em claro do <see cref="ISeccoSecretCipher"/> não vale para chave de arquivo.
/// </para>
/// </remarks>
public sealed class AesGcmFileCipher : ISeccoFileCipher
{
	/// <summary>Tamanho do bloco de conteúdo em claro, em bytes (64 KiB).</summary>
	/// <remarks>
	/// Abaixo do limite de 85 000 bytes do Large Object Heap, mesmo com o tag: os buffers de cada
	/// operação não vão para o LOH.
	/// </remarks>
	public const int ChunkSizeInBytes = 64 * 1024;

	/// <summary>Versão do formato que este tipo grava.</summary>
	public const byte FormatVersion = 1;

	private const int KeySizeInBytes = AesGcmSecretCipher.KeySizeInBytes;
	private const int NonceSizeInBytes = 12;
	private const int TagSizeInBytes = 16;
	private const int SegmentSizeInBytes = ChunkSizeInBytes + TagSizeInBytes;

	private static readonly byte[] Header = [.. "SECCOENC"u8, FormatVersion];

	private readonly ISeccoSecretCipher _keyWrapper;

	/// <summary>Cria o cifrador de arquivo sobre o cifrador que embrulha as chaves dos arquivos.</summary>
	/// <param name="keyWrapper">
	/// Cifrador com a chave mestra ativa e as aposentadas. O produto escolhe se usa a mesma chave
	/// mestra dos segredos em coluna ou uma própria para arquivos.
	/// </param>
	public AesGcmFileCipher(ISeccoSecretCipher keyWrapper)
	{
		ArgumentNullException.ThrowIfNull(keyWrapper);

		_keyWrapper = keyWrapper;
	}

	/// <inheritdoc />
	public async Task<SeccoEncryptedFile> EncryptAsync(
		Stream source,
		Stream destination,
		long? maxPlaintextLength = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);

		if (maxPlaintextLength is < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(maxPlaintextLength), "O limite não pode ser negativo.");
		}

		var fileKey = RandomNumberGenerator.GetBytes(KeySizeInBytes);
		var plaintext = new byte[ChunkSizeInBytes];
		var ciphertext = new byte[ChunkSizeInBytes];
		var lookahead = new byte[1];
		var nonce = new byte[NonceSizeInBytes];
		var tag = new byte[TagSizeInBytes];

		try
		{
			await destination.WriteAsync(Header, cancellationToken).ConfigureAwait(false);

			using var aes = new AesGcm(fileKey, TagSizeInBytes);
			var carried = 0;
			var total = 0L;
			var index = 0UL;

			while (true)
			{
				var length = carried + await source
					.ReadAtLeastAsync(plaintext.AsMemory(carried), ChunkSizeInBytes - carried, throwOnEndOfStream: false, cancellationToken)
					.ConfigureAwait(false);

				// Bloco cheio só é o último se o fluxo acabar logo depois: espiar um byte evita
				// gravar um bloco vazio no fim de todo arquivo de tamanho múltiplo do bloco.
				var isLast = length < ChunkSizeInBytes
					|| await source.ReadAsync(lookahead, cancellationToken).ConfigureAwait(false) == 0;

				total += length;

				if (total > maxPlaintextLength)
				{
					throw new SeccoFileTooLargeException(maxPlaintextLength.Value);
				}

				WriteNonce(nonce, index, isLast);
				aes.Encrypt(nonce, plaintext.AsSpan(0, length), ciphertext.AsSpan(0, length), tag, Header);

				await destination.WriteAsync(ciphertext.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
				await destination.WriteAsync(tag, cancellationToken).ConfigureAwait(false);

				if (isLast)
				{
					return new SeccoEncryptedFile(WrapKey(fileKey), total);
				}

				plaintext[0] = lookahead[0];
				carried = 1;
				index++;
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(fileKey);
			CryptographicOperations.ZeroMemory(plaintext);
			CryptographicOperations.ZeroMemory(lookahead);
		}
	}

	/// <inheritdoc />
	public async Task DecryptAsync(
		Stream source,
		Stream destination,
		string wrappedKey,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		ArgumentNullException.ThrowIfNull(wrappedKey);

		// A chave primeiro: chave desconhecida falha antes de ler um byte do arquivo.
		var fileKey = UnwrapKey(wrappedKey);
		var segment = new byte[SegmentSizeInBytes];
		var plaintext = new byte[ChunkSizeInBytes];
		var lookahead = new byte[1];
		var nonce = new byte[NonceSizeInBytes];

		try
		{
			await ReadHeaderAsync(source, cancellationToken).ConfigureAwait(false);

			using var aes = new AesGcm(fileKey, TagSizeInBytes);
			var carried = 0;
			var index = 0UL;

			while (true)
			{
				var length = carried + await source
					.ReadAtLeastAsync(segment.AsMemory(carried), SegmentSizeInBytes - carried, throwOnEndOfStream: false, cancellationToken)
					.ConfigureAwait(false);

				var isLast = length < SegmentSizeInBytes
					|| await source.ReadAsync(lookahead, cancellationToken).ConfigureAwait(false) == 0;

				if (length < TagSizeInBytes)
				{
					throw new SeccoSecretCipherException("Arquivo cifrado truncado (ADR-0061).");
				}

				var ciphertextLength = length - TagSizeInBytes;
				WriteNonce(nonce, index, isLast);

				try
				{
					aes.Decrypt(
						nonce,
						segment.AsSpan(0, ciphertextLength),
						segment.AsSpan(ciphertextLength, TagSizeInBytes),
						plaintext.AsSpan(0, ciphertextLength),
						Header);
				}
				catch (CryptographicException exception)
				{
					// Bloco alterado, fora de posição, ou o último de um arquivo cortado num limite de
					// bloco: os três aparecem aqui, como tag que não confere.
					throw new SeccoSecretCipherException(
						"Arquivo cifrado adulterado ou truncado: um bloco não autenticou (ADR-0061).",
						exception);
				}

				await destination.WriteAsync(plaintext.AsMemory(0, ciphertextLength), cancellationToken).ConfigureAwait(false);

				if (isLast)
				{
					return;
				}

				segment[0] = lookahead[0];
				carried = 1;
				index++;
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(fileKey);
			CryptographicOperations.ZeroMemory(plaintext);
		}
	}

	/// <inheritdoc />
	public bool IsKeyWrappedWithActiveKey(string wrappedKey)
	{
		ArgumentNullException.ThrowIfNull(wrappedKey);

		// Valor sem o prefixo já é false aqui (legado em claro não é "cifrado com a chave ativa").
		return _keyWrapper.IsEncryptedWithActiveKey(wrappedKey);
	}

	/// <inheritdoc />
	public string RewrapKey(string wrappedKey)
	{
		ArgumentNullException.ThrowIfNull(wrappedKey);

		var fileKey = UnwrapKey(wrappedKey);

		try
		{
			return WrapKey(fileKey);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(fileKey);
		}
	}

	private string WrapKey(byte[] fileKey) => _keyWrapper.Encrypt(Convert.ToBase64String(fileKey));

	private byte[] UnwrapKey(string wrappedKey)
	{
		// O ISeccoSecretCipher devolve como está o valor sem prefixo (legado em claro, ADR-0025).
		// Aqui isso deixaria quem escreve na coluna escolher a chave do arquivo.
		if (!HasWrappedKeyPrefix(wrappedKey))
		{
			throw new SeccoSecretCipherException("Chave de arquivo sem o formato embrulhado (ADR-0061).");
		}

		byte[] fileKey;

		try
		{
			fileKey = Convert.FromBase64String(_keyWrapper.Decrypt(wrappedKey));
		}
		catch (FormatException exception)
		{
			throw new SeccoSecretCipherException("Chave de arquivo com conteúdo inválido (ADR-0061).", exception);
		}

		if (fileKey.Length != KeySizeInBytes)
		{
			CryptographicOperations.ZeroMemory(fileKey);

			throw new SeccoSecretCipherException("Chave de arquivo com tamanho inválido (ADR-0061).");
		}

		return fileKey;
	}

	private static bool HasWrappedKeyPrefix(string wrappedKey) =>
		wrappedKey.StartsWith(AesGcmSecretCipher.Prefix, StringComparison.Ordinal);

	private static async Task ReadHeaderAsync(Stream source, CancellationToken cancellationToken)
	{
		var header = new byte[Header.Length];
		var read = await source
			.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken)
			.ConfigureAwait(false);

		if (read < header.Length || !header.AsSpan().SequenceEqual(Header))
		{
			throw new SeccoSecretCipherException("Arquivo cifrado com cabeçalho inválido ou versão desconhecida (ADR-0061).");
		}
	}

	private static void WriteNonce(Span<byte> nonce, ulong index, bool isLast)
	{
		nonce[..3].Clear();
		BinaryPrimitives.WriteUInt64BigEndian(nonce.Slice(3, sizeof(ulong)), index);
		nonce[NonceSizeInBytes - 1] = isLast ? (byte)1 : (byte)0;
	}
}
