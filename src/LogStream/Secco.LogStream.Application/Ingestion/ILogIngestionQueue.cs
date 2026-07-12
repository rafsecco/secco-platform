using Secco.LogStream.Domain.ApiCalls;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;

namespace Secco.LogStream.Application.Ingestion;

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
/// Porta da fila de ingestão assíncrona (bounded, FIFO única): a API responde <c>202</c>
/// sem esperar o banco; o worker persiste no banco do tenant capturado no enfileiramento.
/// A fila única garante ordem — um processo enfileirado antes dos seus details é
/// persistido antes deles.
/// </summary>
public interface ILogIngestionQueue
{
    /// <summary>Tenta enfileirar um registro de log. Nunca bloqueia.</summary>
    /// <param name="logEntry">Registro a persistir de forma assíncrona.</param>
    EnqueueOutcome TryEnqueue(LogEntry logEntry);

    /// <summary>Tenta enfileirar um processo. Nunca bloqueia.</summary>
    /// <param name="logProcess">Processo a persistir de forma assíncrona.</param>
    EnqueueOutcome TryEnqueue(LogProcess logProcess);

    /// <summary>Tenta enfileirar um detail de processo. Nunca bloqueia.</summary>
    /// <param name="detail">Detail a persistir de forma assíncrona.</param>
    EnqueueOutcome TryEnqueue(LogProcessDetail detail);

    /// <summary>Tenta enfileirar um registro de chamada de API. Nunca bloqueia.</summary>
    /// <param name="apiCallLog">Registro a persistir de forma assíncrona.</param>
    EnqueueOutcome TryEnqueue(ApiCallLog apiCallLog);
}
