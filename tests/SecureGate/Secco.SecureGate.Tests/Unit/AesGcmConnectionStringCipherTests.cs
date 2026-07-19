using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Infrastructure.Cryptography;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Cifragem AES-256-GCM da connection string do catálogo (ADR-0025): roundtrip, detecção de
/// adulteração (AEAD), rotação de chave (aposentada só decifra), passthrough de legado em
/// claro e o formato de armazenamento exato <c>secco-enc:v1:</c>.
/// </summary>
public class AesGcmConnectionStringCipherTests
{
    private const string Plaintext = "Server=tenant-a.interno;Database=logstream;User Id=svc;Password=p@ss w0rd;";

    private static string NewKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmConnectionStringCipher CreateCipher(string activeKey, params string[] retiredKeys) =>
        new(
            Options.Create(new SecureGateCatalogOptions
            {
                EncryptionKey = activeKey,
                RetiredEncryptionKeys = [.. retiredKeys],
            }),
            new FakeHostEnvironment("Production"));

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsThePlaintext()
    {
        var cipher = CreateCipher(NewKey());

        var encrypted = cipher.Encrypt(Plaintext);

        cipher.Decrypt(encrypted).Should().Be(Plaintext);
    }

    [Fact]
    public void Encrypt_Always_ProducesTheExactVersionedPrefix()
    {
        var cipher = CreateCipher(NewKey());

        cipher.Encrypt(Plaintext).Should().StartWith("secco-enc:v1:");
    }

    [Fact]
    public void Encrypt_SameInputTwice_ProducesDistinctCiphertexts()
    {
        var cipher = CreateCipher(NewKey());

        // Nonce aleatório por cifragem: dois valores idênticos não geram o mesmo blob
        var first = cipher.Encrypt(Plaintext);
        var second = cipher.Encrypt(Plaintext);

        first.Should().NotBe(second);
        cipher.Decrypt(first).Should().Be(Plaintext);
        cipher.Decrypt(second).Should().Be(Plaintext);
    }

    [Fact]
    public void Decrypt_WithFlippedByte_FailsWithInfrastructureException()
    {
        var cipher = CreateCipher(NewKey());
        var encrypted = cipher.Encrypt(Plaintext);

        var tampered = FlipOneByteInBlob(encrypted);

        cipher.Invoking(c => c.Decrypt(tampered))
            .Should().Throw<ConnectionStringCipherException>("GCM autentica o dado — adulteração não decifra lixo");
    }

    [Fact]
    public void Decrypt_WithWrongKey_Fails()
    {
        var encrypted = CreateCipher(NewKey()).Encrypt(Plaintext);

        // Outra chave, sem a original nem como aposentada
        var other = CreateCipher(NewKey());

        other.Invoking(c => c.Decrypt(encrypted))
            .Should().Throw<ConnectionStringCipherException>();
    }

    [Fact]
    public void Decrypt_WithRetiredKey_StillDecrypts()
    {
        var retired = NewKey();
        var encrypted = CreateCipher(retired).Encrypt(Plaintext);

        // Chave ativa nova, com a antiga apenas para decifrar (rotação, ADR-0025)
        var rotated = CreateCipher(NewKey(), retired);

        rotated.Decrypt(encrypted).Should().Be(Plaintext);
        rotated.IsEncryptedWithActiveKey(encrypted).Should().BeFalse("foi cifrado com a chave aposentada — converge no startup");
    }

    [Fact]
    public void Decrypt_LegacyPlaintextWithoutPrefix_PassesThrough()
    {
        var cipher = CreateCipher(NewKey());

        cipher.Decrypt(Plaintext).Should().Be(Plaintext, "valor sem prefixo é legado em claro (ADR-0025)");
        cipher.IsEncryptedWithActiveKey(Plaintext).Should().BeFalse();
    }

    [Fact]
    public void Decrypt_UnknownFormatVersion_FailsWithInfrastructureException()
    {
        var cipher = CreateCipher(NewKey());

        cipher.Invoking(c => c.Decrypt("secco-enc:v2:AAAA"))
            .Should().Throw<ConnectionStringCipherException>("versão de formato desconhecida não é decifrável");
    }

    [Fact]
    public void IsEncryptedWithActiveKey_ForFreshlyEncrypted_IsTrue()
    {
        var cipher = CreateCipher(NewKey());

        cipher.IsEncryptedWithActiveKey(cipher.Encrypt(Plaintext)).Should().BeTrue();
    }

    private static string FlipOneByteInBlob(string encrypted)
    {
        const string prefix = "secco-enc:v1:";
        var blob = Convert.FromBase64String(encrypted[prefix.Length..]);

        // Vira um bit no meio do blob (região do ciphertext) — o tag GCM não confere mais
        blob[blob.Length / 2] ^= 0xFF;

        return prefix + Convert.ToBase64String(blob);
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Secco.Tests";

        public string ContentRootPath { get; set; } = string.Empty;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
