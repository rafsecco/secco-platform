using FluentAssertions;
using Secco.LogStream.Application;
using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class CreateLogEntryHandlerTests
{
    private sealed class FakeQueue(EnqueueOutcome outcome) : ILogIngestionQueue
    {
        public List<LogEntry> Enqueued { get; } = [];

        public EnqueueOutcome TryEnqueue(LogEntry logEntry)
        {
            if (outcome == EnqueueOutcome.Enqueued)
            {
                Enqueued.Add(logEntry);
            }

            return outcome;
        }

        public EnqueueOutcome TryEnqueue(LogProcess logProcess) => outcome;

        public EnqueueOutcome TryEnqueue(LogProcessDetail detail) => outcome;

        public EnqueueOutcome TryEnqueue(Secco.LogStream.Domain.ApiCalls.ApiCallLog apiCallLog) => outcome;
    }

    private static readonly LogStreamIngestionOptions Options = new();

    [Fact]
    public void Handle_WithValidCommand_EnqueuesAndReturnsId()
    {
        var queue = new FakeQueue(EnqueueOutcome.Enqueued);
        var handler = new CreateLogEntryHandler(queue, Options);

        var result = handler.Handle(new CreateLogEntryCommand(LogEntryLevel.Error, "Falha X"));

        result.IsSuccess.Should().BeTrue();
        queue.Enqueued.Should().ContainSingle().Which.Id.Should().Be(result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Handle_WithoutMessage_ReturnsValidationFailure(string? message)
    {
        var handler = new CreateLogEntryHandler(new FakeQueue(EnqueueOutcome.Enqueued), Options);

        var result = handler.Handle(new CreateLogEntryCommand(LogEntryLevel.Error, message));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LogStreamErrors.LogEntries.MessageRequired);
    }

    [Fact]
    public void Handle_WithMessageAboveLimit_ReturnsValidationFailure()
    {
        var handler = new CreateLogEntryHandler(new FakeQueue(EnqueueOutcome.Enqueued), Options);

        var result = handler.Handle(new CreateLogEntryCommand(
            LogEntryLevel.Error, new string('x', Options.MaxMessageLength + 1)));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be("LogStream.LogEntry.MessageTooLong");
    }

    [Fact]
    public void Handle_WhenQueueFull_ReturnsUnavailable()
    {
        var handler = new CreateLogEntryHandler(new FakeQueue(EnqueueOutcome.QueueFull), Options);

        var result = handler.Handle(new CreateLogEntryCommand(LogEntryLevel.Error, "Falha X"));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public void Handle_WhenTenantNotResolved_ReturnsValidationFailure()
    {
        var handler = new CreateLogEntryHandler(new FakeQueue(EnqueueOutcome.TenantNotResolved), Options);

        var result = handler.Handle(new CreateLogEntryCommand(LogEntryLevel.Error, "Falha X"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LogStreamErrors.Ingestion.TenantNotResolved);
    }

    [Fact]
    public void HandleBatch_AboveMaxBatchSize_ReturnsValidationFailure()
    {
        var handler = new CreateLogEntryBatchHandler(new FakeQueue(EnqueueOutcome.Enqueued), Options);
        var commands = Enumerable.Range(0, Options.MaxBatchSize + 1)
            .Select(i => new CreateLogEntryCommand(LogEntryLevel.Information, $"msg {i}"))
            .ToList();

        var result = handler.Handle(commands);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("LogStream.LogEntry.BatchTooLarge");
    }

    [Fact]
    public void HandleBatch_WithInvalidItem_FailsWithoutEnqueuingAnything()
    {
        var queue = new FakeQueue(EnqueueOutcome.Enqueued);
        var handler = new CreateLogEntryBatchHandler(queue, Options);

        var result = handler.Handle(
        [
            new CreateLogEntryCommand(LogEntryLevel.Information, "ok"),
            new CreateLogEntryCommand(LogEntryLevel.Information, ""),
        ]);

        result.IsFailure.Should().BeTrue();
        queue.Enqueued.Should().BeEmpty("a validação do batch é tudo-ou-nada");
    }
}
