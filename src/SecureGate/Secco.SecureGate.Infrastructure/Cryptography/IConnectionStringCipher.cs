namespace Secco.SecureGate.Infrastructure.Cryptography;

/// <summary>
/// Cifragem em repouso da connection string do catálogo (ADR-0025): AES-256-GCM na camada
/// de aplicação do SecureGate. O domínio permanece puro (plaintext) — esta é a preocupação
/// de Infrastructure exercida pelo value converter do EF Core. Falhas de decifragem são
/// <b>infraestruturais</b> (<see cref="ConnectionStringCipherException"/>), nunca erros de
/// negócio: dado adulterado ou chave desconhecida não são fluxo esperado.
/// </summary>
public interface IConnectionStringCipher
{
    /// <summary>
    /// Cifra o plaintext com a chave <b>ativa</b>, produzindo o formato versionado e
    /// autodescritivo <c>secco-enc:v1:&lt;base64(nonce ‖ ciphertext ‖ tag)&gt;</c>.
    /// </summary>
    /// <param name="plaintext">Connection string em claro. Obrigatória.</param>
    /// <returns>Valor cifrado no formato de armazenamento.</returns>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decifra um valor armazenado. Valor <b>sem</b> o prefixo <c>secco-enc:</c> é tratado
    /// como legado em claro e devolvido como está. Com o prefixo, tenta a chave ativa e
    /// depois cada chave aposentada; nenhuma servindo, ou formato/versão desconhecidos,
    /// lançam <see cref="ConnectionStringCipherException"/>.
    /// </summary>
    /// <param name="stored">Valor lido da coluna <c>ds_connection_string</c>.</param>
    /// <returns>Connection string em claro.</returns>
    string Decrypt(string stored);

    /// <summary>
    /// Indica se o valor armazenado já está cifrado com a chave <b>ativa</b> — a condição de
    /// convergência da ADR-0025 (legado em claro e cifrado por chave aposentada retornam
    /// <c>false</c> e são re-cifrados no startup).
    /// </summary>
    /// <param name="stored">Valor lido da coluna <c>ds_connection_string</c>.</param>
    /// <returns><c>true</c> se cifrado com a chave ativa; caso contrário, <c>false</c>.</returns>
    bool IsEncryptedWithActiveKey(string stored);
}
