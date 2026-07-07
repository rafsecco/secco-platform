namespace Secco.SharedKernel.Exceptions;

/// <summary>
/// Raiz de todas as exceções lançadas pela Secco Platform. Exceções são reservadas
/// a falhas de infraestrutura e bugs — erros de negócio fluem via <c>Result&lt;T&gt;</c> (ADR-0004).
/// </summary>
public abstract class SeccoException : Exception
{
    /// <summary>Inicializa a exceção sem mensagem específica.</summary>
    protected SeccoException()
    {
    }

    /// <summary>Inicializa a exceção com a mensagem informada.</summary>
    /// <param name="message">Mensagem descrevendo a falha.</param>
    protected SeccoException(string message)
        : base(message)
    {
    }

    /// <summary>Inicializa a exceção com mensagem e exceção interna.</summary>
    /// <param name="message">Mensagem descrevendo a falha.</param>
    /// <param name="innerException">Exceção que causou esta.</param>
    protected SeccoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
