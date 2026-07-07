namespace Secco.SharedKernel.Exceptions;

/// <summary>
/// Violação de invariante de domínio: um estado que jamais deveria ocorrer foi
/// solicitado — bug do chamador, não erro de negócio (esses usam <c>Result&lt;T&gt;</c>,
/// ADR-0004). Guardas de entidades e value objects lançam esta exceção.
/// </summary>
public sealed class DomainInvariantException : SeccoException
{
    /// <summary>Inicializa a exceção sem mensagem específica.</summary>
    public DomainInvariantException()
    {
    }

    /// <summary>Inicializa a exceção com a mensagem informada.</summary>
    /// <param name="message">Descrição da invariante violada.</param>
    public DomainInvariantException(string message)
        : base(message)
    {
    }

    /// <summary>Inicializa a exceção com mensagem e exceção interna.</summary>
    /// <param name="message">Descrição da invariante violada.</param>
    /// <param name="innerException">Exceção que causou esta.</param>
    public DomainInvariantException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
