namespace Secco.SecureGate.Infrastructure.Cryptography;

/// <summary>
/// Falha <b>infraestrutural</b> de cifragem/decifragem do catálogo (ADR-0025): dado
/// adulterado, versão de formato desconhecida ou nenhuma chave (ativa/aposentada) servindo.
/// Não é erro de negócio (não usa <c>Result&lt;T&gt;</c>): é uma quebra de invariante do
/// armazenamento, que deve interromper o fluxo. A mensagem <b>nunca</b> inclui o valor
/// cifrado nem chaves (ADR-0020).
/// </summary>
public sealed class ConnectionStringCipherException : Exception
{
    /// <summary>Cria a exceção com a mensagem informada.</summary>
    /// <param name="message">Descrição da falha — sem segredos.</param>
    public ConnectionStringCipherException(string message)
        : base(message)
    {
    }

    /// <summary>Cria a exceção com a mensagem e a causa raiz.</summary>
    /// <param name="message">Descrição da falha — sem segredos.</param>
    /// <param name="innerException">Causa original (ex.: falha criptográfica).</param>
    public ConnectionStringCipherException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
