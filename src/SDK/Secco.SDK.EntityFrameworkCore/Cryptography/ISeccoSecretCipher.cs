namespace Secco.SDK.EntityFrameworkCore.Cryptography;

/// <summary>
/// Cifragem em repouso de um segredo guardado em coluna (ADR-0025), na camada de aplicação.
/// </summary>
/// <remarks>
/// Nasceu dentro do <c>Secco.SecureGate</c> para a connection string do catálogo e subiu para
/// o SDK quando um segundo produto precisou do mesmo formato: o adotante já havia reimplementado
/// o `secco-enc:` por conta própria, e o terceiro consumidor faria três implementações
/// independentes do mesmo formato criptográfico divergindo entre si.
/// <para>
/// O domínio permanece puro (plaintext); esta é preocupação de Infrastructure, exercida por um
/// value converter do EF Core. Falha de decifragem é <b>infraestrutural</b>
/// (<see cref="SeccoSecretCipherException"/>), nunca erro de negócio: dado adulterado ou chave
/// desconhecida não são fluxo esperado e não viram <c>Result&lt;T&gt;</c> (ADR-0004).
/// </para>
/// </remarks>
public interface ISeccoSecretCipher
{
	/// <summary>
	/// Cifra o plaintext com a chave <b>ativa</b>, no formato versionado e autodescritivo
	/// <c>secco-enc:v1:&lt;base64(nonce ‖ ciphertext ‖ tag)&gt;</c>.
	/// </summary>
	/// <param name="plaintext">Valor em claro. Obrigatório.</param>
	string Encrypt(string plaintext);

	/// <summary>
	/// Decifra um valor armazenado. Valor <b>sem</b> o prefixo <c>secco-enc:</c> é tratado como
	/// legado em claro e devolvido como está. Com o prefixo, tenta a chave ativa e depois cada
	/// chave aposentada; nenhuma servindo, ou formato desconhecido, lança.
	/// </summary>
	/// <param name="stored">Valor lido da coluna.</param>
	/// <exception cref="SeccoSecretCipherException">Dado adulterado, formato ou chave desconhecidos.</exception>
	string Decrypt(string stored);

	/// <summary>
	/// Indica se o valor já está cifrado com a chave <b>ativa</b> — a condição de convergência
	/// da rotação (legado em claro e cifrado por chave aposentada retornam <c>false</c>).
	/// </summary>
	/// <param name="stored">Valor lido da coluna.</param>
	bool IsEncryptedWithActiveKey(string stored);
}
