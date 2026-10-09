namespace Secco.SharedKernel.Exceptions;

/// <summary>
/// Falha TRANSITÓRIA de infraestrutura: o chamador pode tentar de novo (ADR-0038). Base comum
/// para os pacotes do SDK, que não se referenciam, traduzirem a mesma condição em 503 +
/// Retry-After. A mensagem nunca carrega segredo nem connection string (ADR-0020).
/// </summary>
public abstract class SeccoTransientException : SeccoException
{
	/// <summary>Inicializa a exceção sem mensagem específica.</summary>
	protected SeccoTransientException()
	{
	}

	/// <summary>Inicializa a exceção com a mensagem informada.</summary>
	/// <param name="message">Mensagem descrevendo a falha.</param>
	protected SeccoTransientException(string message)
		: base(message)
	{
	}

	/// <summary>Inicializa a exceção com mensagem e exceção interna.</summary>
	/// <param name="message">Mensagem descrevendo a falha.</param>
	/// <param name="innerException">Exceção que causou esta.</param>
	protected SeccoTransientException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
