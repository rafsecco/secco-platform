using Secco.LogStream.Domain.LogEntries;

namespace Secco.LogStream.Application.LogEntries;

/// <summary>Resultado da tentativa de enfileirar um registro para ingestão assíncrona.</summary>
public enum EnqueueOutcome
{
    /// <summary>Aceito na fila — será persistido pelo worker.</summary>
    Enqueued = 0,

    /// <summary>Fila na capacidade máxima — o chamador recebe 503 e decide repetir.</summary>
    QueueFull = 1,

    /// <summary>Requisição sem tenant resolvido — logs sempre pertencem a um tenant (ADR-0005).</summary>
    TenantNotResolved = 2,
}

/// <summary>
/// Porta da fila de ingestão assíncrona (bounded): a API responde <c>202</c> sem esperar o
/// banco; o worker de background persiste no banco do tenant capturado no enfileiramento.
/// </summary>
public interface ILogEntryIngestionQueue
{
    /// <summary>Tenta enfileirar o registro para o tenant da requisição atual. Nunca bloqueia.</summary>
    /// <param name="logEntry">Registro a persistir de forma assíncrona.</param>
    EnqueueOutcome TryEnqueue(LogEntry logEntry);
}
