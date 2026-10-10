using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Permissions;
using Secco.SharedKernel.Authorization;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Validação e efeitos da publicação do catálogo de permissões (ADR-0039).</summary>
public class PublishPermissionCatalogHandlerTests
{
	private sealed class CapturingLogger : ILogger<PublishPermissionCatalogHandler>
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Entries.Add((logLevel, formatter(state, exception)));
	}

	private readonly IPermissionCatalogRepository _repository = Substitute.For<IPermissionCatalogRepository>();
	private readonly CapturingLogger _logger = new();
	private readonly PublishPermissionCatalogHandler _handler;

	public PublishPermissionCatalogHandlerTests()
	{
		_repository
			.ReplaceAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<SeccoPermissionDefinition>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
			.Returns([]);
		_handler = new PublishPermissionCatalogHandler(_repository, _logger);
	}

	private static PublishPermissionCatalogCommand Command(string? product, params (string? Name, string? Description)[] permissions) =>
		new(product, [.. permissions.Select(p => new PermissionDefinitionInput(p.Name, p.Description))]);

	private async Task AssertNotPersistedAsync() =>
		await _repository.DidNotReceiveWithAnyArgs()
			.ReplaceAsync(default!, default!, default, default);

	[Fact]
	public async Task HandleAsync_ValidCatalog_ReplacesWithTrimmedDescriptions()
	{
		var result = await _handler.HandleAsync(Command("logstream", ("log-entries:read", "  Ler logs  ")));

		result.IsSuccess.Should().BeTrue();
		await _repository.Received(1).ReplaceAsync(
			"logstream",
			Arg.Is<IReadOnlyList<SeccoPermissionDefinition>>(p => p.Count == 1 && p[0] == new SeccoPermissionDefinition("log-entries:read", "Ler logs")),
			Arg.Any<DateTimeOffset>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task HandleAsync_EmptyList_IsValid()
	{
		var result = await _handler.HandleAsync(Command("logstream"));

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task HandleAsync_NullList_ReturnsPermissionsRequired()
	{
		var result = await _handler.HandleAsync(new PublishPermissionCatalogCommand("logstream", null));

		result.Error.Should().Be(SecureGateErrors.PermissionCatalog.PermissionsRequired);
		await AssertNotPersistedAsync();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("LogStream")]
	[InlineData("log stream")]
	[InlineData("-logstream")]
	[InlineData("logstream-")]
	[InlineData("a--b")]
	public async Task HandleAsync_InvalidProduct_ReturnsProductInvalid(string? product)
	{
		var result = await _handler.HandleAsync(Command(product));

		result.Error.Should().Be(SecureGateErrors.PermissionCatalog.ProductInvalid);
		await AssertNotPersistedAsync();
	}

	[Fact]
	public async Task HandleAsync_ProductOverMaxLength_ReturnsProductInvalid()
	{
		var result = await _handler.HandleAsync(Command(new string('a', 51)));

		result.Error.Should().Be(SecureGateErrors.PermissionCatalog.ProductInvalid);
	}

	[Fact]
	public async Task HandleAsync_ExactlyMaxEntries_IsValid()
	{
		var entries = Enumerable.Range(0, 200).Select(i => ((string?)$"res-{i}:read", (string?)"d")).ToArray();

		var result = await _handler.HandleAsync(Command("logstream", entries));

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task HandleAsync_OverMaxEntries_ReturnsTooManyPermissions()
	{
		var entries = Enumerable.Range(0, 201).Select(i => ((string?)$"res-{i}:read", (string?)"d")).ToArray();

		var result = await _handler.HandleAsync(Command("logstream", entries));

		result.Error.Should().Be(SecureGateErrors.PermissionCatalog.TooManyPermissions);
		await AssertNotPersistedAsync();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("semseparador")]
	[InlineData("Log-Entries:Read")]
	[InlineData("a:b:c")]
	public async Task HandleAsync_InvalidName_ReturnsPermissionNameInvalid(string? name)
	{
		var result = await _handler.HandleAsync(Command("logstream", (name, "descrição")));

		result.Error.Should().Be(SecureGateErrors.PermissionCatalog.PermissionNameInvalid);
		await AssertNotPersistedAsync();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("linha\ncom quebra")]
	[InlineData("com\ttab")]
	[InlineData("com\u0000nulo")]
	public async Task HandleAsync_InvalidDescription_ReturnsDescriptionInvalid(string? description)
	{
		var result = await _handler.HandleAsync(Command("logstream", ("log-entries:read", description)));

		result.Error.Should().Be(SecureGateErrors.PermissionCatalog.DescriptionInvalid);
		await AssertNotPersistedAsync();
	}

	[Fact]
	public async Task HandleAsync_DescriptionLimits_200IsValid201IsNot()
	{
		var ok = await _handler.HandleAsync(Command("logstream", ("log-entries:read", new string('d', 200))));
		var tooLong = await _handler.HandleAsync(Command("logstream", ("log-entries:read", new string('d', 201))));

		ok.IsSuccess.Should().BeTrue();
		tooLong.Error.Should().Be(SecureGateErrors.PermissionCatalog.DescriptionInvalid);
	}

	[Fact]
	public async Task HandleAsync_DescriptionLongOnlyBeforeTrim_IsValid()
	{
		var result = await _handler.HandleAsync(Command("logstream", ("log-entries:read", "  " + new string('d', 200) + "  ")));

		result.IsSuccess.Should().BeTrue("o limite vale após o trim");
	}

	[Fact]
	public async Task HandleAsync_DuplicateName_ReturnsDuplicateName()
	{
		var result = await _handler.HandleAsync(Command("logstream", ("log-entries:read", "a"), ("log-entries:read", "b")));

		result.Error.Should().Be(SecureGateErrors.PermissionCatalog.DuplicateName);
		await AssertNotPersistedAsync();
	}

	[Fact]
	public async Task HandleAsync_NameCollidingWithAnotherProduct_LogsWarningWithProductAndNameOnly_AndSucceeds()
	{
		_repository
			.ReplaceAsync("notificationhub", Arg.Any<IReadOnlyList<SeccoPermissionDefinition>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
			.Returns(["x:read"]);

		var result = await _handler.HandleAsync(Command("notificationhub", ("x:read", "SEGREDO-DA-DESCRICAO-INTEIRA")));

		result.IsSuccess.Should().BeTrue("colisão é visível, não bloqueia (ADR-0039)");
		var warning = _logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
		warning.Message.Should().Contain("notificationhub").And.Contain("x:read");
		warning.Message.Should().NotContain("SEGREDO-DA-DESCRICAO-INTEIRA");
	}

	[Fact]
	public async Task HandleAsync_WithoutCollision_LogsNoWarning()
	{
		await _handler.HandleAsync(Command("logstream", ("log-entries:read", "Ler")));

		_logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
	}
}
