using FluentAssertions;
using Secco.LogStream.Application;
using Secco.LogStream.Application.Audit;
using Secco.LogStream.Domain.Audit;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class CreateAuditEntryHandlerTests
{
	private sealed class FakeRepository : IAuditEntryRepository
	{
		public List<AuditEntry> Added { get; } = [];

		public Task AddAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default)
		{
			Added.Add(auditEntry);
			return Task.CompletedTask;
		}

		public Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
			Task.FromResult(Added.SingleOrDefault(e => e.Id == id));

		public Task<PagedResult<AuditEntry>> SearchAsync(AuditEntrySearchCriteria criteria, CancellationToken cancellationToken = default) =>
			Task.FromResult(PagedResult.Create((IReadOnlyList<AuditEntry>)Added, PageRequest.Default, Added.Count));
	}

	private static readonly LogStreamIngestionOptions Options = new();

	private static CreateAuditEntryCommand ValidCommand(string? metadata = null) =>
		new("user-123", ActorType.User, "documento.download", Metadata: metadata);

	[Fact]
	public async Task HandleAsync_WithValidCommand_PersistsAndReturnsDto()
	{
		var repository = new FakeRepository();
		var handler = new CreateAuditEntryHandler(repository, Options);

		var result = await handler.HandleAsync(ValidCommand());

		result.IsSuccess.Should().BeTrue();
		repository.Added.Should().ContainSingle().Which.Id.Should().Be(result.Value.Id);
	}

	[Fact]
	public async Task HandleAsync_WithoutActorId_ReturnsValidationFailure()
	{
		var handler = new CreateAuditEntryHandler(new FakeRepository(), Options);

		var result = await handler.HandleAsync(new CreateAuditEntryCommand(null, ActorType.User, "documento.download"));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(LogStreamErrors.AuditEntries.ActorIdRequired);
	}

	[Fact]
	public async Task HandleAsync_WithoutAction_ReturnsValidationFailure()
	{
		var handler = new CreateAuditEntryHandler(new FakeRepository(), Options);

		var result = await handler.HandleAsync(new CreateAuditEntryCommand("user-123", ActorType.User, null));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(LogStreamErrors.AuditEntries.ActionRequired);
	}

	[Theory]
	[InlineData("não é json")]
	[InlineData("{ \"unclosed\": ")]
	[InlineData("[1, 2,]")]
	public async Task HandleAsync_WhenMetadataIsNotValidJson_ReturnsFailure(string metadata)
	{
		var handler = new CreateAuditEntryHandler(new FakeRepository(), Options);

		var result = await handler.HandleAsync(ValidCommand(metadata));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(LogStreamErrors.AuditEntries.MetadataInvalidJson);
	}

	[Fact]
	public async Task HandleAsync_WhenMetadataExceedsLimit_ReturnsFailure()
	{
		var handler = new CreateAuditEntryHandler(new FakeRepository(), Options);
		var oversized = "{\"padding\":\"" + new string('x', Options.MaxAuditMetadataLength) + "\"}";

		var result = await handler.HandleAsync(ValidCommand(oversized));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(LogStreamErrors.AuditEntries.MetadataTooLong(Options.MaxAuditMetadataLength));
	}

	[Fact]
	public async Task HandleAsync_WithValidMetadataJson_PersistsIt()
	{
		var repository = new FakeRepository();
		var handler = new CreateAuditEntryHandler(repository, Options);

		var result = await handler.HandleAsync(ValidCommand("{\"documentId\":42}"));

		result.IsSuccess.Should().BeTrue();
		repository.Added.Single().Metadata.Should().Be("{\"documentId\":42}");
	}

	[Fact]
	public async Task HandleAsync_WithActorIdAboveLimit_ReturnsValidationFailure()
	{
		var handler = new CreateAuditEntryHandler(new FakeRepository(), Options);

		var result = await handler.HandleAsync(new CreateAuditEntryCommand(
			new string('x', Options.MaxAuditActorIdLength + 1), ActorType.User, "documento.download"));

		result.IsFailure.Should().BeTrue();
		result.Error.Type.Should().Be(ErrorType.Validation);
		result.Error.Code.Should().Be("LogStream.AuditEntry.ActorIdTooLong");
	}
}
