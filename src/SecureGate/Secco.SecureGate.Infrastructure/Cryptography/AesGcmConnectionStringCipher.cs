using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Secco.SecureGate.Infrastructure.Cryptography;

/// <summary>
/// Cifragem AES-256-GCM da connection string do catálogo (ADR-0025). Formato de
/// armazenamento: <c>secco-enc:v1:&lt;base64(nonce ‖ ciphertext ‖ tag)&gt;</c> — versionado
/// e autodescritivo, sem dependência externa (<see cref="AesGcm"/> nativo). GCM é AEAD: o
/// tag autentica o dado, então adulteração falha em vez de decifrar lixo.
/// </summary>
internal sealed class AesGcmConnectionStringCipher : IConnectionStringCipher
{
    /// <summary>Prefixo que marca um valor cifrado (o que não o tem é legado em claro).</summary>
    internal const string Prefix = "secco-enc:";

    /// <summary>Prefixo completo da versão 1 do formato.</summary>
    internal const string VersionOnePrefix = $"{Prefix}v1:";

    private const int NonceSizeInBytes = 12;  // 96 bits — recomendado para AES-GCM
    private const int TagSizeInBytes = 16;    // 128 bits — máximo do GCM

    private readonly byte[] _activeKey;
    private readonly IReadOnlyList<byte[]> _retiredKeys;

    public AesGcmConnectionStringCipher(IOptions<SecureGateCatalogOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        var value = options.Value;

        var activeBase64 = !string.IsNullOrWhiteSpace(value.EncryptionKey)
            ? value.EncryptionKey
            : environment.IsProduction()
                // Inalcançável na prática: o validator falha o startup antes (ADR-0025). Defesa em profundidade.
                ? throw new ConnectionStringCipherException(
                    "Chave de cifragem do catálogo ausente em Production (ADR-0025).")
                : SecureGateCatalogOptions.DevelopmentEncryptionKey;

        _activeKey = DecodeKey(activeBase64);
        _retiredKeys = [.. value.RetiredEncryptionKeys.Select(DecodeKey)];
    }

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

    public string Decrypt(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        // Legado em claro: aceito na leitura, convergido no startup (ADR-0025)
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

        // Sem valor (nem chave ativa nem aposentada) — dado adulterado ou chave desconhecida
        throw new ConnectionStringCipherException(
            "Não foi possível decifrar a connection string: dado adulterado ou chave desconhecida (ADR-0025).");
    }

    public bool IsEncryptedWithActiveKey(string stored)
    {
        ArgumentNullException.ThrowIfNull(stored);

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Formato/versão inválidos não são "cifrados com a chave ativa" — a convergência
        // então chamará Decrypt, que falha alto (ADR-0025); não mascaramos aqui.
        if (!stored.StartsWith(VersionOnePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return TryParseVersionOneBlob(stored, out var blob) && TryDecrypt(_activeKey, blob, out _);
    }

    private static byte[] ParseVersionOneBlob(string stored)
    {
        if (!TryParseVersionOneBlob(stored, out var blob))
        {
            throw new ConnectionStringCipherException(
                "Versão de formato de cifragem desconhecida ou base64 inválido (ADR-0025).");
        }

        return blob;
    }

    private static bool TryParseVersionOneBlob(string stored, out byte[] blob)
    {
        blob = [];

        if (!stored.StartsWith(VersionOnePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var base64 = stored[VersionOnePrefix.Length..];

        try
        {
            var decoded = Convert.FromBase64String(base64);

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
            // Tag não confere para esta chave: dado adulterado OU chave errada — o chamador tenta a próxima
            return false;
        }
    }

    private static byte[] DecodeKey(string base64Key)
    {
        byte[] key;

        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException exception)
        {
            throw new ConnectionStringCipherException(
                "Chave de cifragem do catálogo em base64 inválido (ADR-0025).", exception);
        }

        return key.Length == SecureGateCatalogOptions.KeySizeInBytes
            ? key
            : throw new ConnectionStringCipherException(
                $"Chave de cifragem do catálogo deve ter {SecureGateCatalogOptions.KeySizeInBytes} bytes (AES-256) (ADR-0025).");
    }
}
