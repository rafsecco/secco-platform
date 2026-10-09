using Secco.SharedKernel.Exceptions;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// O banco do tenant não pôde ser preparado (ADR-0038) — transitória: o SDK responde 503 +
/// Retry-After, e a próxima abertura tenta de novo. Mensagem fixa: nunca a connection string.
/// </summary>
public sealed class TenantDatabaseUnavailableException : SeccoTransientException
{
	/// <summary>Inicializa a exceção a partir da falha original.</summary>
	/// <param name="innerException">Falha do EF/ADO.NET ao migrar.</param>
	public TenantDatabaseUnavailableException(Exception innerException)
		: base("O banco de dados do tenant está temporariamente indisponível.", innerException)
	{
	}
}
