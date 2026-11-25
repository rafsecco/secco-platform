namespace Secco.SDK.EntityFrameworkCore.Cryptography;

/// <summary>
/// Cifragem em envelope de arquivo em repouso, em streaming (ADR-0061): chave própria por arquivo,
/// conteúdo em blocos AES-256-GCM e a chave do arquivo embrulhada por um
/// <see cref="ISeccoSecretCipher"/> no formato <c>secco-enc:v1:</c>.
/// </summary>
/// <remarks>
/// O produto guarda dois valores: o arquivo cifrado (disco, blob) e a chave embrulhada (coluna).
/// Os dois só servem juntos. Rotacionar a chave mestra reembrulha a chave do arquivo
/// (<see cref="RewrapKey"/>) e não regrava conteúdo nenhum.
/// <para>
/// Falha de decifragem é <b>infraestrutural</b> (<see cref="SeccoSecretCipherException"/>), nunca
/// <c>Result&lt;T&gt;</c> (ADR-0004): adulteração, truncamento e chave desconhecida não são fluxo
/// esperado.
/// </para>
/// </remarks>
public interface ISeccoFileCipher
{
	/// <summary>
	/// Cifra <paramref name="source"/> em <paramref name="destination"/> com uma chave sorteada para
	/// este arquivo, e devolve essa chave já embrulhada pela chave mestra ativa.
	/// </summary>
	/// <param name="source">Conteúdo em claro. Lido até o fim; não é descartado.</param>
	/// <param name="destination">Destino do conteúdo cifrado. Não é descartado.</param>
	/// <param name="maxPlaintextLength">
	/// Tamanho máximo aceito do conteúdo em claro, em bytes. Ultrapassá-lo interrompe a cifragem
	/// com <see cref="SeccoFileTooLargeException"/>. Nulo = sem limite.
	/// </param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Chave embrulhada e tamanho do conteúdo em claro.</returns>
	/// <exception cref="SeccoFileTooLargeException">Conteúdo acima de <paramref name="maxPlaintextLength"/>.</exception>
	/// <remarks>
	/// Em qualquer exceção, <paramref name="destination"/> fica com um conteúdo parcial que não
	/// decifra. Apagá-lo é de quem chama.
	/// </remarks>
	Task<SeccoEncryptedFile> EncryptAsync(
		Stream source,
		Stream destination,
		long? maxPlaintextLength = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Decifra <paramref name="source"/> em <paramref name="destination"/>, bloco a bloco. Cada bloco
	/// só é escrito depois de autenticado.
	/// </summary>
	/// <param name="source">Conteúdo cifrado. Não é descartado.</param>
	/// <param name="destination">Destino do conteúdo em claro. Não é descartado.</param>
	/// <param name="wrappedKey">Chave do arquivo, como devolvida por <see cref="EncryptAsync"/>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <exception cref="SeccoSecretCipherException">
	/// Chave embrulhada inválida ou de chave mestra desconhecida, formato desconhecido, bloco
	/// adulterado, blocos fora de ordem ou arquivo truncado.
	/// </exception>
	/// <remarks>
	/// Truncamento só é detectado no fim. Quando a exceção vem no meio, <paramref name="destination"/>
	/// já recebeu os blocos anteriores, todos autênticos. Quem escreve numa resposta HTTP deve tratar
	/// a exceção como resposta abortada, nunca como download completo.
	/// </remarks>
	Task DecryptAsync(
		Stream source,
		Stream destination,
		string wrappedKey,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Indica se a chave do arquivo está embrulhada pela chave mestra <b>ativa</b> — a condição de
	/// convergência da rotação.
	/// </summary>
	/// <param name="wrappedKey">Chave embrulhada lida da coluna.</param>
	bool IsKeyWrappedWithActiveKey(string wrappedKey);

	/// <summary>
	/// Reembrulha a chave do arquivo com a chave mestra ativa. O arquivo cifrado não muda.
	/// </summary>
	/// <param name="wrappedKey">Chave embrulhada pela chave ativa ou por uma aposentada.</param>
	/// <returns>A mesma chave de arquivo, embrulhada pela chave ativa.</returns>
	/// <exception cref="SeccoSecretCipherException">Chave embrulhada inválida ou de chave mestra desconhecida.</exception>
	string RewrapKey(string wrappedKey);
}

/// <summary>Resultado da cifragem de um arquivo.</summary>
/// <param name="WrappedKey">Chave do arquivo embrulhada, no formato <c>secco-enc:v1:</c>. Guardar junto dos metadados.</param>
/// <param name="PlaintextLength">Tamanho do conteúdo em claro, em bytes.</param>
public sealed record SeccoEncryptedFile(string WrappedKey, long PlaintextLength);
