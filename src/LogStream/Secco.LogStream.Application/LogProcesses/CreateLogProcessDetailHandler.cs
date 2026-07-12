using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.LogProcesses;

/// <summary>Comando de criação de um detail de processo (o pai vem da rota).</summary>
/// <param name="Level">Severidade do passo.</param>
/// <param name="Message">Mensagem. Obrigatória.</param>
/// <param name="StackTrace">Stack trace, quando houver.</param>
/// <param name="CorrelationId">Correlation id da requisição de origem (populado pela borda).</param>
public sealed record CreateLogProcessDetailCommand(
    LogEntryLevel Level,
    string? Message,
    string? StackTrace = null,
    Guid? CorrelationId = null);

/// <summary>
/// Valida e enfileira details de um processo (unitário e lote). A existência do pai não é
/// verificada de forma síncrona — a ingestão é fire-and-forget (<c>202</c>): um detail de
/// processo inexistente falha no worker e é logado, nunca bloqueia o chamador.
/// </summary>
public sealed class CreateLogProcessDetailHandler(ILogIngestionQueue queue, LogStreamIngestionOptions options)
{
    /// <summary>Executa o caso de uso para um detail.</summary>
    /// <param name="logProcessId">Processo pai (da rota).</param>
    /// <param name="command">Comando de criação.</param>
    public Result<Guid> Handle(Guid logProcessId, CreateLogProcessDetailCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = Validate(command, options);

        if (validation.IsFailure)
        {
            return Result.Failure<Guid>(validation.Error);
        }

        var detail = new LogProcessDetail(logProcessId, command.Level, command.Message!, command.StackTrace, command.CorrelationId);

        return queue.TryEnqueue(detail) switch
        {
            EnqueueOutcome.Enqueued => detail.Id,
            EnqueueOutcome.QueueFull => LogStreamErrors.Ingestion.QueueFull,
            _ => LogStreamErrors.Ingestion.TenantNotResolved,
        };
    }

    /// <summary>Executa o caso de uso para um lote de details (validação tudo-ou-nada).</summary>
    /// <param name="logProcessId">Processo pai (da rota).</param>
    /// <param name="commands">Itens do lote.</param>
    public Result<IReadOnlyList<Guid>> HandleBatch(Guid logProcessId, IReadOnlyList<CreateLogProcessDetailCommand> commands)
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
            var validation = Validate(command, options);

            if (validation.IsFailure)
            {
                return Result.Failure<IReadOnlyList<Guid>>(validation.Error);
            }
        }

        var ids = new List<Guid>(commands.Count);

        foreach (var command in commands)
        {
            var detail = new LogProcessDetail(logProcessId, command.Level, command.Message!, command.StackTrace, command.CorrelationId);
            var outcome = queue.TryEnqueue(detail);

            if (outcome != EnqueueOutcome.Enqueued)
            {
                return Result.Failure<IReadOnlyList<Guid>>(outcome == EnqueueOutcome.QueueFull
                    ? LogStreamErrors.Ingestion.QueueFull
                    : LogStreamErrors.Ingestion.TenantNotResolved);
            }

            ids.Add(detail.Id);
        }

        return ids;
    }

    private static Result Validate(CreateLogProcessDetailCommand command, LogStreamIngestionOptions options) =>
        CreateLogEntryHandler.Validate(
            new CreateLogEntryCommand(command.Level, command.Message, command.StackTrace, command.CorrelationId),
            options);
}
