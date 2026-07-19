namespace Secco.SecureGate.Infrastructure.Cryptography;

/// <summary>
/// Configuração da cifragem do catálogo (ADR-0025), seção <c>SecureGate:Catalog</c>. A
/// <see cref="EncryptionKey"/> (32 bytes, base64) cifra e decifra a connection string; as
/// <see cref="RetiredEncryptionKeys"/> só decifram (rotação — o startup converge tudo para
/// a ativa). Regras validadas no startup, fail-fast (ADR-0020): em Production a chave é
/// obrigatória e a <see cref="DevelopmentEncryptionKey"/> embutida é proibida — espelha o
/// padrão da <c>DevelopmentSigningKey</c> (ADR-0022).
/// </summary>
public sealed class SecureGateCatalogOptions
{
    /// <summary>Seção de configuração de onde estas opções são lidas.</summary>
    public const string SectionKey = "SecureGate:Catalog";

    /// <summary>Tamanho exigido da chave mestra, em bytes (AES-256).</summary>
    public const int KeySizeInBytes = 32;

    /// <summary>
    /// Chave de desenvolvimento embutida (well-known, base64 de 32 bytes) usada quando
    /// <see cref="EncryptionKey"/> está ausente <b>fora de Production</b> — zero-config em DEV,
    /// como o certificado de desenvolvimento do OpenIddict. <b>Proibida em Production</b>.
    /// </summary>
    public const string DevelopmentEncryptionKey = "c2VjY28tc2VjdXJlZ2F0ZS1kZXYtY2F0YWxvZy1rZXk=";

    /// <summary>Chave mestra ativa (base64, 32 bytes). Obrigatória em Production.</summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Chaves aposentadas (base64, 32 bytes cada) mantidas <b>só para decifrar</b> durante a
    /// rotação. O re-encrypt idempotente do startup converge as linhas para a chave ativa.
    /// </summary>
    public IList<string> RetiredEncryptionKeys { get; set; } = [];
}
