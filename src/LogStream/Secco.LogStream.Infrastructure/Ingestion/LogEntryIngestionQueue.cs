using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.LogStream.Infrastructure.Ingestion;

/// <summary>
/// Adaptador (escopo de request) da porta de ingestão: captura o tenant atual no
/// enfileiramento — o worker roda fora do request e precisa saber em qual banco gravar.
/// </summary>
internal sealed class LogEntryIngestionQueue(LogEntryIngestionChannel channel, ITenantContext tenantContext)
    : ILogIngestionQueue
{
    public EnqueueOutcome TryEnqueue(LogEntry logEntry)
    {
        ArgumentNullException.ThrowIfNull(logEntry);

        return Enqueue(tenantId => new LogEntryWorkItem(tenantId, logEntry));
    }

    public EnqueueOutcome TryEnqueue(LogProcess logProcess)
    {
        ArgumentNullException.ThrowIfNull(logProcess);

        return Enqueue(tenantId => new LogProcessWorkItem(tenantId, logProcess));
    }

    public EnqueueOutcome TryEnqueue(LogProcessDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return Enqueue(tenantId => new LogProcessDetailWorkItem(tenantId, detail));
    }

    private EnqueueOutcome Enqueue(Func<Guid, IngestionWorkItem> workItemFactory)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return EnqueueOutcome.TenantNotResolved;
        }

        return channel.Writer.TryWrite(workItemFactory(tenantId))
            ? EnqueueOutcome.Enqueued
            : EnqueueOutcome.QueueFull;
    }
}
