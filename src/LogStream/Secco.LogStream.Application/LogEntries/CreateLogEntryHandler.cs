using Secco.LogStream.Domain.LogEntries;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.LogEntries;

/// <summary>Comando de criação de um registro de log.</summary>
/// <param name="Level">Severidade.</param>
/// <param name="Message">Mensagem do log.</param>
/// <param name="StackTrace">Stack trace, quando houver.</param>
/// <param name="CorrelationId">Correlation id da requisição de origem (populado pela borda).</param>
public sealed record CreateLogEntryCommand(
    LogEntryLevel Level,
    string? Message,
    string? StackTrace = null,
    Guid? CorrelationId = null);

/// <summary>
/// Valida os limites de ingestão (ADR-0020) e enfileira o registro — a persistência é
/// assíncrona (worker), por isso o sucesso devolve apenas o Id (Guid v7 já definitivo).
/// </summary>
public sealed class CreateLogEntryHandler(ILogEntryIngestionQueue queue, LogStreamIngestionOptions options)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de criação.</param>
    public Result<Guid> Handle(CreateLogEntryCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = Validate(command, options);

        if (validation.IsFailure)
        {
            return Result.Failure<Guid>(validation.Error);
        }

        var logEntry = new LogEntry(command.Level, command.Message!, command.StackTrace, command.CorrelationId);

        return queue.TryEnqueue(logEntry) switch
        {
            EnqueueOutcome.Enqueued => logEntry.Id,
            EnqueueOutcome.QueueFull => LogStreamErrors.Ingestion.QueueFull,
            _ => LogStreamErrors.Ingestion.TenantNotResolved,
        };
    }

    /// <summary>Validação de um item, compartilhada com o batch.</summary>
    internal static Result Validate(CreateLogEntryCommand command, LogStreamIngestionOptions options)
    {
        if (string.IsNullOrWhiteSpace(command.Message))
        {
            return Result.Failure(LogStreamErrors.LogEntries.MessageRequired);
        }

        if (command.Message.Length > options.MaxMessageLength)
        {
            return Result.Failure(LogStreamErrors.LogEntries.MessageTooLong(options.MaxMessageLength));
        }

        if (command.StackTrace is not null && command.StackTrace.Length > options.MaxStackTraceLength)
        {
            return Result.Failure(LogStreamErrors.LogEntries.StackTraceTooLong(options.MaxStackTraceLength));
        }

        return Result.Success();
    }
}
