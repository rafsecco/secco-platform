using Secco.LogStream.Domain.LogEntries;
using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.LogStream.Domain.LogProcesses;

/// <summary>
/// Passo/evento de um <see cref="LogProcess"/>. O nível alimenta o status agregado do pai.
/// </summary>
public sealed class LogProcessDetail : BaseEntity
{
    private LogProcessDetail()
    {
        // Construtor de rehidratação do EF Core
        Message = string.Empty;
    }

    /// <summary>Cria um detail vinculado a um processo.</summary>
    /// <param name="logProcessId">Processo pai (o chamador o conhece desde o <c>202</c> da criação).</param>
    /// <param name="level">Severidade do passo.</param>
    /// <param name="message">Mensagem. Obrigatória.</param>
    /// <param name="stackTrace">Stack trace associado, quando houver.</param>
    /// <param name="correlationId">Correlation id da requisição de origem, quando propagado.</param>
    /// <exception cref="DomainInvariantException">Se o processo for vazio ou a mensagem for nula/vazia.</exception>
    public LogProcessDetail(Guid logProcessId, LogEntryLevel level, string message, string? stackTrace = null, Guid? correlationId = null)
    {
        if (logProcessId == Guid.Empty)
        {
            throw new DomainInvariantException("Um detail exige o processo pai.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new DomainInvariantException("Um detail exige mensagem não vazia.");
        }

        LogProcessId = logProcessId;
        Level = level;
        Message = message;
        StackTrace = stackTrace;
        CorrelationId = correlationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Processo pai (coluna <c>id_fk_log_process</c>).</summary>
    public Guid LogProcessId { get; private set; }

    /// <summary>Severidade do passo (coluna <c>ie_level</c>).</summary>
    public LogEntryLevel Level { get; private set; }

    /// <summary>Mensagem (coluna <c>ds_message</c>).</summary>
    public string Message { get; private set; }

    /// <summary>Stack trace associado (coluna <c>ds_stack_trace</c>).</summary>
    public string? StackTrace { get; private set; }

    /// <summary>Correlation id da requisição de origem (coluna <c>correlation_id</c>).</summary>
    public Guid? CorrelationId { get; private set; }

    /// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
