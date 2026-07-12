using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Domain.LogProcesses;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.LogProcesses;

/// <summary>Comando de criação de um processo.</summary>
/// <param name="Name">Nome do processo. Obrigatório.</param>
/// <param name="ExternalReference">Referência de negócio do chamador, sem semântica imposta.</param>
/// <param name="CorrelationId">Correlation id da requisição de origem (populado pela borda).</param>
public sealed record CreateLogProcessCommand(string? Name, string? ExternalReference = null, Guid? CorrelationId = null);

/// <summary>
/// Valida e enfileira um processo — ingestão assíncrona: o <c>202</c> devolve o Id definitivo
/// (Guid v7), que o chamador usa imediatamente para enviar os details (a fila FIFO única
/// garante que o pai é persistido antes deles).
/// </summary>
public sealed class CreateLogProcessHandler(ILogIngestionQueue queue, LogStreamIngestionOptions options)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de criação.</param>
    public Result<Guid> Handle(CreateLogProcessCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            return LogStreamErrors.LogProcesses.NameRequired;
        }

        if (command.Name.Length > options.MaxProcessNameLength)
        {
            return LogStreamErrors.LogProcesses.NameTooLong(options.MaxProcessNameLength);
        }

        if (command.ExternalReference is not null && command.ExternalReference.Length > options.MaxExternalReferenceLength)
        {
            return LogStreamErrors.LogProcesses.ExternalReferenceTooLong(options.MaxExternalReferenceLength);
        }

        var logProcess = new LogProcess(command.Name, command.ExternalReference, command.CorrelationId);

        return queue.TryEnqueue(logProcess) switch
        {
            EnqueueOutcome.Enqueued => logProcess.Id,
            EnqueueOutcome.QueueFull => LogStreamErrors.Ingestion.QueueFull,
            _ => LogStreamErrors.Ingestion.TenantNotResolved,
        };
    }
}
