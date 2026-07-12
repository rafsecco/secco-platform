using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Domain.LogEntries;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.LogStream.Infrastructure.Ingestion;

/// <summary>
/// Adaptador (escopo de request) da porta de ingestão: captura o tenant atual no
/// enfileiramento — o worker roda fora do request e precisa saber em qual banco gravar.
/// </summary>
internal sealed class LogEntryIngestionQueue(LogEntryIngestionChannel channel, ITenantContext tenantContext)
    : ILogEntryIngestionQueue
{
    public EnqueueOutcome TryEnqueue(LogEntry logEntry)
    {
        ArgumentNullException.ThrowIfNull(logEntry);

        if (tenantContext.TenantId is not { } tenantId)
        {
            return EnqueueOutcome.TenantNotResolved;
        }

        return channel.Writer.TryWrite(new LogEntryWorkItem(tenantId, logEntry))
            ? EnqueueOutcome.Enqueued
            : EnqueueOutcome.QueueFull;
    }
}
