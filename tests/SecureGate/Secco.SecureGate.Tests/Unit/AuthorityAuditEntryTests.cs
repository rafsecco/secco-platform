using FluentAssertions;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SharedKernel.Exceptions;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Invariantes da entrada da trilha de autoridade (ADR-0054): tenant obrigatório e limites de tamanho.</summary>
public class AuthorityAuditEntryTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);

	[Fact]
	public void Valida_GuardaOInstanteRecebido()
	{
		var entry = Create();

		entry.CreatedAt.Should().Be(Now);
		entry.Id.Should().NotBeEmpty();
	}

	[Fact]
	public void TenantVazio_Falha()
	{
		var act = () => Create(Guid.Empty);

		act.Should().Throw<DomainInvariantException>();
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void AtorOuAcaoEmBranco_Falha(string blank)
	{
		var actor = () => Create(actorId: blank);
		var action = () => Create(action: blank);

		actor.Should().Throw<DomainInvariantException>();
		action.Should().Throw<DomainInvariantException>();
	}

	[Fact]
	public void AcimaDosLimites_Falha()
	{
		var actor = () => Create(actorId: new string('a', AuthorityAuditEntry.ActorIdMaxLength + 1));
		var action = () => Create(action: new string('a', AuthorityAuditEntry.ActionMaxLength + 1));
		var metadata = () => Create(metadata: new string('m', AuthorityAuditEntry.MetadataMaxLength + 1));

		actor.Should().Throw<DomainInvariantException>();
		action.Should().Throw<DomainInvariantException>();
		metadata.Should().Throw<DomainInvariantException>();
	}

	private static AuthorityAuditEntry Create(
		Guid? tenant = null, string actorId = "cli", string action = "gestao-do-tenant.CreateRole", string? metadata = "{}") =>
		new(tenant ?? Guid.CreateVersion7(), "client", actorId, null, action, "tenant", "id", metadata, Now);
}
