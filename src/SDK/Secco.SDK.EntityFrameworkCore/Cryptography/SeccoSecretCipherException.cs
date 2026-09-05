namespace Secco.SDK.EntityFrameworkCore.Cryptography;

/// <summary>
/// Falha <b>infraestrutural</b> de cifragem ou decifragem (ADR-0025): dado adulterado, versão de
/// formato desconhecida, ou nenhuma chave — ativa ou aposentada — servindo.
/// </summary>
/// <remarks>
/// Não é erro de negócio e por isso não usa <c>Result&lt;T&gt;</c> (ADR-0004): é quebra de
/// invariante do armazenamento, que deve interromper o fluxo em vez de virar um caminho
/// alternativo. A mensagem <b>nunca</b> inclui o valor cifrado nem material de chave (ADR-0020).
/// </remarks>
public sealed class SeccoSecretCipherException : Exception
{
	/// <summary>Cria a exceção com a mensagem informada.</summary>
	/// <param name="message">Descrição da falha — sem segredos.</param>
	public SeccoSecretCipherException(string message)
		: base(message)
	{
	}

	/// <summary>Cria a exceção com a mensagem e a causa raiz.</summary>
	/// <param name="message">Descrição da falha — sem segredos.</param>
	/// <param name="innerException">Causa original (ex.: falha criptográfica).</param>
	public SeccoSecretCipherException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
