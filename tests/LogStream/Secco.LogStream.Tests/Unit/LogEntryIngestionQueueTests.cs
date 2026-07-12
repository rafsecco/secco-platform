using FluentAssertions;
using Secco.LogStream.Application;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Infrastructure.Ingestion;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class LogEntryIngestionQueueTests
{
    private sealed class FakeTenantContext(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;

        public bool IsResolved => tenantId is not null;
    }

    private static LogEntry NewEntry() => new(LogEntryLevel.Information, "msg");

    [Fact]
    public void TryEnqueue_WithResolvedTenantAndCapacity_ReturnsEnqueued()
    {
        var channel = new LogEntryIngestionChannel(new LogStreamIngestionOptions { QueueCapacity = 1 });
        var queue = new LogEntryIngestionQueue(channel, new FakeTenantContext(Guid.NewGuid()));

        queue.TryEnqueue(NewEntry()).Should().Be(EnqueueOutcome.Enqueued);
    }

    [Fact]
    public void TryEnqueue_WhenChannelIsFull_ReturnsQueueFull()
    {
        var channel = new LogEntryIngestionChannel(new LogStreamIngestionOptions { QueueCapacity = 1 });
        var queue = new LogEntryIngestionQueue(channel, new FakeTenantContext(Guid.NewGuid()));

        queue.TryEnqueue(NewEntry()).Should().Be(EnqueueOutcome.Enqueued);
        queue.TryEnqueue(NewEntry()).Should().Be(EnqueueOutcome.QueueFull,
            "fila bounded cheia rejeita em vez de descartar silenciosamente (ADR-0020)");
    }

    [Fact]
    public void TryEnqueue_WithoutResolvedTenant_ReturnsTenantNotResolved()
    {
        var channel = new LogEntryIngestionChannel(new LogStreamIngestionOptions());
        var queue = new LogEntryIngestionQueue(channel, new FakeTenantContext(null));

        queue.TryEnqueue(NewEntry()).Should().Be(EnqueueOutcome.TenantNotResolved);
    }
}
