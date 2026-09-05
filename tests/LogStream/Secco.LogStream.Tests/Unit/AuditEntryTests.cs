using FluentAssertions;
using Secco.LogStream.Domain.Audit;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class AuditEntryTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Create_WhenActorIdMissing_ThrowsDomainInvariant(string? actorId)
	{
		var act = () => new AuditEntry(actorId!, ActorType.User, "documento.download");

		act.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Create_WhenActionMissing_ThrowsDomainInvariant(string? action)
	{
		var act = () => new AuditEntry("user-123", ActorType.User, action!);

		act.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void Create_WithoutOccurredAt_UsesCurrentMomentAndCreatedAtIndependently()
	{
		var before = DateTimeOffset.UtcNow;

		var entry = new AuditEntry("user-123", ActorType.User, "documento.download");

		var after = DateTimeOffset.UtcNow;

		entry.OccurredAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
		entry.CreatedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
	}

	[Fact]
	public void Create_WithOccurredAtDeclared_KeepsItDistinctFromCreatedAt()
	{
		var occurredAt = DateTimeOffset.UtcNow.AddDays(-2);

		var entry = new AuditEntry("user-123", ActorType.User, "documento.download", occurredAt: occurredAt);

		entry.OccurredAt.Should().Be(occurredAt);
		entry.CreatedAt.Should().BeAfter(occurredAt, "CreatedAt é sempre o carimbo do servidor, distinto do fato declarado");
	}

	[Fact]
	public void Create_Always_IsImmutable()
	{
		var entry = new AuditEntry(
			"client-abc", ActorType.Client, "tenant.database.rotate",
			actorName: "Job de rotação", resourceType: "tenant-database", resourceId: "logstream",
			metadata: "{\"reason\":\"scheduled\"}", correlationId: Guid.NewGuid());

		entry.ActorId.Should().Be("client-abc");
		entry.ActorType.Should().Be(ActorType.Client);
		entry.Action.Should().Be("tenant.database.rotate");
		entry.ActorName.Should().Be("Job de rotação");
		entry.ResourceType.Should().Be("tenant-database");
		entry.ResourceId.Should().Be("logstream");
		entry.Metadata.Should().Be("{\"reason\":\"scheduled\"}");
	}
}
