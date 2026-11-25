using System.Buffers.Binary;
using System.Security.Cryptography;
using FluentAssertions;
using Secco.SDK.EntityFrameworkCore.Cryptography;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Cryptography;

/// <summary>
/// Cifragem de arquivo em streaming (ADR-0061): roundtrip em todos os limites de bloco, o formato
/// documentado, adulteração, reordenação, repetição e truncamento, chave embrulhada recusada fora
/// do formato, rotação sem regravar o arquivo e limite de tamanho.
/// </summary>
public class AesGcmFileCipherTests
{
	private const int Chunk = AesGcmFileCipher.ChunkSizeInBytes;
	private const int Tag = 16;
	private const int HeaderLength = 9;

	private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

	private static AesGcmFileCipher CreateCipher(byte[] masterKey, params byte[][] retired) =>
		new(new AesGcmSecretCipher(masterKey, retired));

	private static byte[] Content(int length) => RandomNumberGenerator.GetBytes(length);

	private static async Task<(byte[] Encrypted, SeccoEncryptedFile Result)> EncryptAsync(
		AesGcmFileCipher cipher, byte[] content, long? max = null)
	{
		using var destination = new MemoryStream();
		var result = await cipher.EncryptAsync(new MemoryStream(content), destination, max);

		return (destination.ToArray(), result);
	}

	private static async Task<byte[]> DecryptAsync(AesGcmFileCipher cipher, byte[] encrypted, string wrappedKey)
	{
		using var destination = new MemoryStream();
		await cipher.DecryptAsync(new MemoryStream(encrypted), destination, wrappedKey);

		return destination.ToArray();
	}

	private static int ExpectedChunks(int length) => length == 0 ? 1 : (length + Chunk - 1) / Chunk;

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(Chunk - 1)]
	[InlineData(Chunk)]
	[InlineData(Chunk + 1)]
	[InlineData((3 * Chunk) - 1)]
	[InlineData(3 * Chunk)]
	[InlineData((3 * Chunk) + 7)]
	public async Task EncryptThenDecrypt_AnySizeAroundChunkBoundaries_RoundTripsWithExactOverhead(int length)
	{
		var cipher = CreateCipher(NewKey());
		var content = Content(length);

		var (encrypted, result) = await EncryptAsync(cipher, content);

		// Tamanho exato: cabeçalho + conteúdo + um tag por bloco. Múltiplo do bloco não ganha
		// bloco vazio no fim.
		encrypted.Should().HaveCount(HeaderLength + length + (Tag * ExpectedChunks(length)));
		result.PlaintextLength.Should().Be(length);
		result.WrappedKey.Should().StartWith("secco-enc:v1:");
		(await DecryptAsync(cipher, encrypted, result.WrappedKey)).Should().Equal(content);
	}

	[Fact]
	public async Task EncryptThenDecrypt_StreamsThatReturnFewBytesPerRead_RoundTrips()
	{
		// Fluxo de rede devolve menos que o pedido: nenhuma leitura pode supor bloco cheio.
		var cipher = CreateCipher(NewKey());
		var content = Content((2 * Chunk) + 123);

		using var encrypted = new MemoryStream();
		var result = await cipher.EncryptAsync(new TricklingStream(content, maxPerRead: 977), encrypted);

		using var decrypted = new MemoryStream();
		await cipher.DecryptAsync(new TricklingStream(encrypted.ToArray(), maxPerRead: 1013), decrypted, result.WrappedKey);

		decrypted.ToArray().Should().Equal(content);
	}

	[Fact]
	public async Task Decrypt_FileBuiltByHandFromTheDocumentedFormat_RoundTrips()
	{
		// Monta o arquivo seguindo só a especificação do formato v1. Se a implementação mudar o
		// formato, arquivo já gravado deixa de abrir — e este teste quebra antes.
		var masterKey = NewKey();
		var secretCipher = new AesGcmSecretCipher(masterKey);
		var fileKey = NewKey();
		var content = Content(Chunk + 10);
		byte[] header = [.. "SECCOENC"u8, 0x01];

		using var file = new MemoryStream();
		file.Write(header);

		using (var aes = new AesGcm(fileKey, Tag))
		{
			var chunks = new[] { content.AsMemory(0, Chunk), content.AsMemory(Chunk) };

			for (var i = 0; i < chunks.Length; i++)
			{
				var nonce = new byte[12];
				BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(3, 8), (ulong)i);
				nonce[11] = i == chunks.Length - 1 ? (byte)1 : (byte)0;

				var ciphertext = new byte[chunks[i].Length];
				var tag = new byte[Tag];
				aes.Encrypt(nonce, chunks[i].Span, ciphertext, tag, header);

				file.Write(ciphertext);
				file.Write(tag);
			}
		}

		var wrappedKey = secretCipher.Encrypt(Convert.ToBase64String(fileKey));

		(await DecryptAsync(new AesGcmFileCipher(secretCipher), file.ToArray(), wrappedKey)).Should().Equal(content);
	}

	[Fact]
	public async Task Decrypt_ChunkNotBoundToTheHeader_Throws()
	{
		// Mesmo arquivo do formato v1, mas com o bloco cifrado sem o cabeçalho como dado
		// associado: o cabeçalho tem de estar amarrado a cada bloco, senão uma versão futura com
		// campos nele poderia ser trocada sem o tag perceber.
		var secretCipher = new AesGcmSecretCipher(NewKey());
		var fileKey = NewKey();
		var content = Content(10);
		var nonce = new byte[12];
		nonce[11] = 1;
		var ciphertext = new byte[content.Length];
		var tag = new byte[Tag];

		using (var aes = new AesGcm(fileKey, Tag))
		{
			aes.Encrypt(nonce, content, ciphertext, tag);
		}

		byte[] file = [.. "SECCOENC"u8, 0x01, .. ciphertext, .. tag];
		var wrappedKey = secretCipher.Encrypt(Convert.ToBase64String(fileKey));

		await FluentActions.Awaiting(() => DecryptAsync(new AesGcmFileCipher(secretCipher), file, wrappedKey))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Encrypt_Always_StartsWithTheVersionedHeaderAndHidesThePlaintext()
	{
		var cipher = CreateCipher(NewKey());
		var content = Enumerable.Repeat((byte)'A', 4096).ToArray();

		var (encrypted, _) = await EncryptAsync(cipher, content);

		encrypted.AsSpan(0, HeaderLength).ToArray().Should().Equal([.. "SECCOENC"u8, 0x01]);
		encrypted.AsSpan(HeaderLength).IndexOf(content.AsSpan(0, 64)).Should().Be(-1);
	}

	[Fact]
	public async Task Encrypt_SameContentTwice_UsesDistinctFileKeysAndCiphertexts()
	{
		var cipher = CreateCipher(NewKey());
		var content = Content(1000);

		var (first, firstResult) = await EncryptAsync(cipher, content);
		var (second, secondResult) = await EncryptAsync(cipher, content);

		// Nonce determinístico só é seguro com chave nova por arquivo.
		first.Should().NotEqual(second);
		firstResult.WrappedKey.Should().NotBe(secondResult.WrappedKey);
	}

	[Theory]
	[InlineData(0)]                                 // cabeçalho
	[InlineData(HeaderLength)]                      // primeiro byte cifrado
	[InlineData(HeaderLength + Chunk + 3)]          // tag do primeiro bloco
	[InlineData(HeaderLength + Chunk + Tag + 100)]  // segundo bloco
	public async Task Decrypt_AnyByteFlipped_Throws(int position)
	{
		var cipher = CreateCipher(NewKey());
		var (encrypted, result) = await EncryptAsync(cipher, Content(Chunk + 500));

		encrypted[position] ^= 0x01;

		await FluentActions.Awaiting(() => DecryptAsync(cipher, encrypted, result.WrappedKey))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Decrypt_FirstChunkTampered_WritesNothing()
	{
		var cipher = CreateCipher(NewKey());
		var (encrypted, result) = await EncryptAsync(cipher, Content(Chunk + 500));
		encrypted[HeaderLength + 10] ^= 0x01;

		using var destination = new MemoryStream();
		var act = () => cipher.DecryptAsync(new MemoryStream(encrypted), destination, result.WrappedKey);

		await act.Should().ThrowAsync<SeccoSecretCipherException>();
		destination.Length.Should().Be(0, "nenhum byte sai antes de o bloco autenticar");
	}

	[Theory]
	[InlineData(0)]                                   // vazio
	[InlineData(HeaderLength - 1)]                    // cabeçalho cortado
	[InlineData(HeaderLength)]                        // só o cabeçalho
	[InlineData(HeaderLength + Tag - 1)]              // menos que um tag
	[InlineData(HeaderLength + 1000)]                 // no meio do primeiro bloco
	[InlineData(HeaderLength + Chunk + Tag)]          // exatamente no limite de bloco
	[InlineData(HeaderLength + (2 * (Chunk + Tag)))]  // limite do segundo bloco
	[InlineData(HeaderLength + (2 * (Chunk + Tag)) + 1)]
	public async Task Decrypt_TruncatedAnywhere_Throws(int keptBytes)
	{
		var cipher = CreateCipher(NewKey());
		var (encrypted, result) = await EncryptAsync(cipher, Content((3 * Chunk) + 50));

		var truncated = encrypted.AsSpan(0, keptBytes).ToArray();

		await FluentActions.Awaiting(() => DecryptAsync(cipher, truncated, result.WrappedKey))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Decrypt_ChunksSwapped_Throws()
	{
		var cipher = CreateCipher(NewKey());
		var (encrypted, result) = await EncryptAsync(cipher, Content((3 * Chunk) + 50));

		var first = Segment(encrypted, 0);
		var second = Segment(encrypted, 1);
		first.CopyTo(encrypted, HeaderLength + (Chunk + Tag));
		second.CopyTo(encrypted, HeaderLength);

		await FluentActions.Awaiting(() => DecryptAsync(cipher, encrypted, result.WrappedKey))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Decrypt_ChunkRepeated_Throws()
	{
		var cipher = CreateCipher(NewKey());
		var (encrypted, result) = await EncryptAsync(cipher, Content((2 * Chunk) + 50));

		byte[] repeated = [.. encrypted.AsSpan(0, HeaderLength + Chunk + Tag), .. Segment(encrypted, 0), .. encrypted.AsSpan(HeaderLength + Chunk + Tag)];

		await FluentActions.Awaiting(() => DecryptAsync(cipher, repeated, result.WrappedKey))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Decrypt_DataAppended_Throws()
	{
		var cipher = CreateCipher(NewKey());
		var (encrypted, result) = await EncryptAsync(cipher, Content(Chunk));

		// O último bloco era cheio; com algo depois, ele passa a ser lido como intermediário.
		byte[] appended = [.. encrypted, .. new byte[Tag]];

		await FluentActions.Awaiting(() => DecryptAsync(cipher, appended, result.WrappedKey))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Decrypt_WrappedKeyOfAnotherFile_Throws()
	{
		var cipher = CreateCipher(NewKey());
		var (encrypted, _) = await EncryptAsync(cipher, Content(100));
		var (_, other) = await EncryptAsync(cipher, Content(100));

		await FluentActions.Awaiting(() => DecryptAsync(cipher, encrypted, other.WrappedKey))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Decrypt_UnknownMasterKey_ThrowsBeforeReadingTheFile()
	{
		var (encrypted, result) = await EncryptAsync(CreateCipher(NewKey()), Content(100));
		var source = new MemoryStream(encrypted);

		var act = () => CreateCipher(NewKey()).DecryptAsync(source, new MemoryStream(), result.WrappedKey);

		await act.Should().ThrowAsync<SeccoSecretCipherException>();
		source.Position.Should().Be(0);
	}

	[Fact]
	public async Task Decrypt_WrappedKeyInPlainBase64_IsRejectedNotTreatedAsLegacy()
	{
		// O ISeccoSecretCipher devolve como está o valor sem prefixo. Se a chave de arquivo
		// herdasse isso, quem escreve na coluna escolheria a chave.
		var masterKey = NewKey();
		var secretCipher = new AesGcmSecretCipher(masterKey);
		var fileKey = NewKey();
		var cipher = new AesGcmFileCipher(secretCipher);
		var (encrypted, _) = await EncryptAsync(cipher, Content(100));

		var act = () => DecryptAsync(cipher, encrypted, Convert.ToBase64String(fileKey));

		await act.Should().ThrowAsync<SeccoSecretCipherException>();
		cipher.IsKeyWrappedWithActiveKey(Convert.ToBase64String(fileKey)).Should().BeFalse();
		FluentActions.Invoking(() => cipher.RewrapKey(Convert.ToBase64String(fileKey)))
			.Should().Throw<SeccoSecretCipherException>();
	}

	[Theory]
	[InlineData("não é base64")]
	[InlineData("AAAA")] // base64 válido, 3 bytes
	public async Task Decrypt_WrappedValueThatIsNotA32ByteKey_Throws(string wrappedContent)
	{
		var secretCipher = new AesGcmSecretCipher(NewKey());
		var cipher = new AesGcmFileCipher(secretCipher);
		var (encrypted, _) = await EncryptAsync(cipher, Content(100));

		await FluentActions.Awaiting(() => DecryptAsync(cipher, encrypted, secretCipher.Encrypt(wrappedContent)))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task Decrypt_UnknownFormatVersion_Throws()
	{
		var cipher = CreateCipher(NewKey());
		var (encrypted, result) = await EncryptAsync(cipher, Content(100));
		encrypted[8] = 0x02;

		await FluentActions.Awaiting(() => DecryptAsync(cipher, encrypted, result.WrappedKey))
			.Should().ThrowAsync<SeccoSecretCipherException>();
	}

	[Fact]
	public async Task RewrapKey_AfterMasterKeyRotation_ConvergesWithoutRewritingTheFile()
	{
		var oldKey = NewKey();
		var newKey = NewKey();
		var content = Content(Chunk + 1);
		var (encrypted, result) = await EncryptAsync(CreateCipher(oldKey), content);

		var rotating = CreateCipher(newKey, oldKey);
		rotating.IsKeyWrappedWithActiveKey(result.WrappedKey).Should().BeFalse();
		(await DecryptAsync(rotating, encrypted, result.WrappedKey)).Should().Equal(content);

		var rewrapped = rotating.RewrapKey(result.WrappedKey);

		rotating.IsKeyWrappedWithActiveKey(rewrapped).Should().BeTrue();
		// Aposentada removida: o arquivo, intocado, abre só com a chave reembrulhada.
		(await DecryptAsync(CreateCipher(newKey), encrypted, rewrapped)).Should().Equal(content);
	}

	[Fact]
	public async Task IsKeyWrappedWithActiveKey_FreshlyEncrypted_IsTrue()
	{
		var cipher = CreateCipher(NewKey());
		var (_, result) = await EncryptAsync(cipher, Content(10));

		cipher.IsKeyWrappedWithActiveKey(result.WrappedKey).Should().BeTrue();
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(Chunk, Chunk)]
	[InlineData((2 * Chunk) + 5, (2 * Chunk) + 5)]
	public async Task Encrypt_ContentExactlyAtTheLimit_Succeeds(int length, long max)
	{
		var cipher = CreateCipher(NewKey());
		var content = Content(length);

		var (encrypted, result) = await EncryptAsync(cipher, content, max);

		(await DecryptAsync(cipher, encrypted, result.WrappedKey)).Should().Equal(content);
	}

	[Theory]
	[InlineData(1, 0)]
	[InlineData(Chunk + 1, Chunk)]
	[InlineData((3 * Chunk) + 1, 100)]
	public async Task Encrypt_ContentAboveTheLimit_ThrowsTooLarge(int length, long max)
	{
		var cipher = CreateCipher(NewKey());

		var act = () => EncryptAsync(cipher, Content(length), max);

		(await act.Should().ThrowAsync<SeccoFileTooLargeException>()).Which.MaxPlaintextLength.Should().Be(max);
	}

	[Fact]
	public async Task Encrypt_NegativeLimit_ThrowsArgumentOutOfRange()
	{
		var cipher = CreateCipher(NewKey());

		await FluentActions.Awaiting(() => EncryptAsync(cipher, Content(1), -1))
			.Should().ThrowAsync<ArgumentOutOfRangeException>();
	}

	[Fact]
	public async Task Encrypt_Cancelled_Throws()
	{
		var cipher = CreateCipher(NewKey());
		using var cancellation = new CancellationTokenSource();
		await cancellation.CancelAsync();

		await FluentActions.Awaiting(() => cipher.EncryptAsync(new MemoryStream(Content(10)), new MemoryStream(), cancellationToken: cancellation.Token))
			.Should().ThrowAsync<OperationCanceledException>();
	}

	private static byte[] Segment(byte[] encrypted, int index) =>
		encrypted.AsSpan(HeaderLength + (index * (Chunk + Tag)), Chunk + Tag).ToArray();

	/// <summary>Fluxo só de leitura, sem busca, que devolve no máximo N bytes por leitura.</summary>
	private sealed class TricklingStream(byte[] content, int maxPerRead) : Stream
	{
		private int _position;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

		public override int Read(Span<byte> buffer)
		{
			var length = Math.Min(Math.Min(buffer.Length, maxPerRead), content.Length - _position);
			content.AsSpan(_position, length).CopyTo(buffer);
			_position += length;

			return length;
		}

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
			ValueTask.FromResult(Read(buffer.Span));

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
