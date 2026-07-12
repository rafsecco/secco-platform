using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Domain.LogEntries;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.LogEntries;

/// <summary>
/// Ingestão em lote: valida todos os itens antes de enfileirar qualquer um (tudo-ou-nada
/// na validação). Se a fila encher no meio do enfileiramento, a falha é reportada e o
/// chamador reenvia o lote inteiro — logs duplicados são preferíveis a logs perdidos
/// (idempotência de escrita está no backlog de ADRs).
/// </summary>
public sealed class CreateLogEntryBatchHandler(ILogIngestionQueue queue, LogStreamIngestionOptions options)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="commands">Itens do lote.</param>
    public Result<IReadOnlyList<Guid>> Handle(IReadOnlyList<CreateLogEntryCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        if (commands.Count == 0)
        {
            return Result.Failure<IReadOnlyList<Guid>>(LogStreamErrors.LogEntries.BatchEmpty);
        }

        if (commands.Count > options.MaxBatchSize)
        {
            return Result.Failure<IReadOnlyList<Guid>>(LogStreamErrors.LogEntries.BatchTooLarge(options.MaxBatchSize));
        }

        foreach (var command in commands)
        {
            var validation = CreateLogEntryHandler.Validate(command, options);

            if (validation.IsFailure)
            {
                return Result.Failure<IReadOnlyList<Guid>>(validation.Error);
            }
        }

        var ids = new List<Guid>(commands.Count);

        foreach (var command in commands)
        {
            var logEntry = new LogEntry(command.Level, command.Message!, command.StackTrace, command.CorrelationId);
            var outcome = queue.TryEnqueue(logEntry);

            if (outcome != EnqueueOutcome.Enqueued)
            {
                return Result.Failure<IReadOnlyList<Guid>>(outcome == EnqueueOutcome.QueueFull
                    ? LogStreamErrors.Ingestion.QueueFull
                    : LogStreamErrors.Ingestion.TenantNotResolved);
            }

            ids.Add(logEntry.Id);
        }

        return ids;
    }
}
