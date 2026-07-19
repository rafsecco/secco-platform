using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Secco.SecureGate.Infrastructure.Cryptography;

/// <summary>
/// Fail-fast de configuração da cifragem do catálogo no startup (ADR-0025/0020): o SecureGate
/// jamais sobe com chave inválida, e jamais sobe em Production sem chave ou com a chave de
/// desenvolvimento embutida — espelha o <c>SeccoAuthenticationOptionsValidator</c> do SDK.
/// </summary>
internal sealed class SecureGateCatalogOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<SecureGateCatalogOptions>
{
    public ValidateOptionsResult Validate(string? name, SecureGateCatalogOptions options)
    {
        var failures = new List<string>();

        var hasKey = !string.IsNullOrWhiteSpace(options.EncryptionKey);
        var usesDevelopmentKey =
            hasKey && string.Equals(options.EncryptionKey, SecureGateCatalogOptions.DevelopmentEncryptionKey, StringComparison.Ordinal);

        if (environment.IsProduction())
        {
            if (!hasKey)
            {
                failures.Add(
                    $"'{SecureGateCatalogOptions.SectionKey}:EncryptionKey' é obrigatória em Production — o catálogo não sobe sem chave de cifragem (fail-fast, ADR-0025).");
            }
            else if (usesDevelopmentKey)
            {
                failures.Add(
                    "A DevelopmentEncryptionKey embutida é proibida em Production — configure uma chave própria (ADR-0025).");
            }
        }

        if (hasKey && !usesDevelopmentKey && !DecodesToKeySize(options.EncryptionKey!))
        {
            failures.Add(
                $"'{SecureGateCatalogOptions.SectionKey}:EncryptionKey' deve ser base64 de exatamente {SecureGateCatalogOptions.KeySizeInBytes} bytes (AES-256).");
        }

        for (var index = 0; index < options.RetiredEncryptionKeys.Count; index++)
        {
            if (!DecodesToKeySize(options.RetiredEncryptionKeys[index]))
            {
                failures.Add(
                    $"'{SecureGateCatalogOptions.SectionKey}:RetiredEncryptionKeys[{index}]' deve ser base64 de exatamente {SecureGateCatalogOptions.KeySizeInBytes} bytes (AES-256).");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static bool DecodesToKeySize(string base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[SecureGateCatalogOptions.KeySizeInBytes + 1];

        return Convert.TryFromBase64String(base64Key, buffer, out var written)
            && written == SecureGateCatalogOptions.KeySizeInBytes;
    }
}
