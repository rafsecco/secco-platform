using FluentAssertions;
using Secco.LogStream.Application;
using Secco.LogStream.Application.ApiCalls;
using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Domain.ApiCalls;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class CreateApiCallLogHandlerTests
{
    private sealed class FakeQueue : ILogIngestionQueue
    {
        public List<ApiCallLog> Enqueued { get; } = [];

        public EnqueueOutcome TryEnqueue(LogEntry logEntry) => EnqueueOutcome.Enqueued;

        public EnqueueOutcome TryEnqueue(LogProcess logProcess) => EnqueueOutcome.Enqueued;

        public EnqueueOutcome TryEnqueue(LogProcessDetail detail) => EnqueueOutcome.Enqueued;

        public EnqueueOutcome TryEnqueue(ApiCallLog apiCallLog)
        {
            Enqueued.Add(apiCallLog);
            return EnqueueOutcome.Enqueued;
        }
    }

    private static readonly LogStreamIngestionOptions Options = new();

    private static CreateApiCallLogCommand ValidCommand(
        IReadOnlyDictionary<string, string?>? headers = null,
        string? requestBody = null) =>
        new("https://api.exemplo.com/pedidos", "post", IsSuccess: true,
            RequestBody: requestBody, RequestHeaders: headers);

    [Fact]
    public void Handle_WithValidCommand_EnqueuesWithNormalizedMethod()
    {
        var queue = new FakeQueue();
        var handler = new CreateApiCallLogHandler(queue, Options);

        var result = handler.Handle(ValidCommand());

        result.IsSuccess.Should().BeTrue();
        queue.Enqueued.Should().ContainSingle().Which.HttpMethod.Should().Be("POST");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-uma-url")]
    [InlineData("/relativa/apenas")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://arquivos.exemplo.com/x")]
    public void Handle_WithInvalidUrl_ReturnsValidationFailure(string? url)
    {
        var handler = new CreateApiCallLogHandler(new FakeQueue(), Options);

        var result = handler.Handle(new CreateApiCallLogCommand(url, "GET", true));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Theory]
    [InlineData("INVENTED")]
    [InlineData("")]
    [InlineData(null)]
    public void Handle_WithUnknownHttpMethod_ReturnsValidationFailure(string? method)
    {
        var handler = new CreateApiCallLogHandler(new FakeQueue(), Options);

        var result = handler.Handle(new CreateApiCallLogCommand("https://api.exemplo.com", method, true));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LogStreamErrors.ApiCalls.MethodInvalid);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void Handle_WithStatusCodeOutOfRange_ReturnsValidationFailure(int statusCode)
    {
        var handler = new CreateApiCallLogHandler(new FakeQueue(), Options);

        var result = handler.Handle(new CreateApiCallLogCommand(
            "https://api.exemplo.com", "GET", false, ResponseStatusCode: statusCode));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(LogStreamErrors.ApiCalls.StatusCodeOutOfRange);
    }

    [Fact]
    public void Handle_WithSensitiveHeaders_RedactsThemBeforeEnqueueing()
    {
        var queue = new FakeQueue();
        var handler = new CreateApiCallLogHandler(queue, Options);

        var result = handler.Handle(ValidCommand(headers: new Dictionary<string, string?>
        {
            ["authorization"] = "Bearer segredo-que-nao-pode-vazar",
            ["Content-Type"] = "application/json",
        }));

        result.IsSuccess.Should().BeTrue();

        var persisted = queue.Enqueued.Single().RequestHeaders!;
        persisted.Should().NotContain("segredo-que-nao-pode-vazar",
            "headers da blocklist são redigidos no servidor, sem confiar no chamador (ADR-0020)");
        persisted.Should().Contain("[REDACTED]");
        persisted.Should().Contain("application/json", "headers inofensivos são preservados");
    }

    [Fact]
    public void Handle_WithConfiguredExtraHeader_RedactsItToo()
    {
        var options = new LogStreamIngestionOptions();
        options.RedactedHeaders.Add("X-Internal-Token");
        var queue = new FakeQueue();
        var handler = new CreateApiCallLogHandler(queue, options);

        handler.Handle(ValidCommand(headers: new Dictionary<string, string?>
        {
            ["x-internal-token"] = "outro-segredo",
        }));

        queue.Enqueued.Single().RequestHeaders.Should().NotContain("outro-segredo");
    }

    [Fact]
    public void Handle_WithBodyAboveLimit_TruncatesWithMarker()
    {
        var queue = new FakeQueue();
        var handler = new CreateApiCallLogHandler(queue, Options);

        handler.Handle(ValidCommand(requestBody: new string('x', Options.MaxBodyLength + 100)));

        var persisted = queue.Enqueued.Single().RequestBody!;
        persisted.Length.Should().Be(Options.MaxBodyLength + BodyTruncator.TruncationMarker.Length);
        persisted.Should().EndWith(BodyTruncator.TruncationMarker);
    }
}
