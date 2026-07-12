using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.LogStream.Domain.LogEntries;

/// <summary>
/// Registro de log geral de uma aplicação. Imutável após a criação — logs não são editados.
/// O tenant não é atributo da entidade: o isolamento é físico, por banco (ADR-0005).
/// O Id Guid v7 (herdado) existe antes do INSERT — é o que permite a ingestão assíncrona
/// responder <c>202</c> já com o identificador definitivo.
/// </summary>
public sealed class LogEntry : BaseEntity
{
    private LogEntry()
    {
        // Construtor de rehidratação do EF Core
        Message = string.Empty;
    }

    /// <summary>Cria um registro de log com o momento de ocorrência corrente.</summary>
    /// <param name="level">Severidade do registro.</param>
    /// <param name="message">Mensagem do log. Obrigatória.</param>
    /// <param name="stackTrace">Stack trace associado, quando houver.</param>
    /// <param name="correlationId">Correlation id da requisição de origem, quando propagado.</param>
    /// <exception cref="DomainInvariantException">Se a mensagem for nula ou vazia.</exception>
    public LogEntry(LogEntryLevel level, string message, string? stackTrace = null, Guid? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new DomainInvariantException("Um registro de log exige mensagem não vazia.");
        }

        Level = level;
        Message = message;
        StackTrace = stackTrace;
        CorrelationId = correlationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Severidade do registro (coluna <c>ie_level</c>).</summary>
    public LogEntryLevel Level { get; private set; }

    /// <summary>Mensagem do log (coluna <c>ds_message</c>).</summary>
    public string Message { get; private set; }

    /// <summary>Stack trace associado (coluna <c>ds_stack_trace</c>).</summary>
    public string? StackTrace { get; private set; }

    /// <summary>Correlation id da requisição de origem (coluna <c>correlation_id</c>).</summary>
    public Guid? CorrelationId { get; private set; }

    /// <summary>Momento da criação do registro (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
