using FluentAssertions;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Vínculo pessoa ↔ tenant (ADR-0040): nasce ativo, valida ids, suspende e reativa.</summary>
public class TenantMembershipTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

	[Fact]
	public void Construtor_IdsValidos_NasceAtivoComOsDadosInformados()
	{
		var userId = Guid.NewGuid();
		var tenantId = Guid.NewGuid();

		var membership = new TenantMembership(userId, tenantId, Now);

		membership.UserId.Should().Be(userId);
		membership.TenantId.Should().Be(tenantId);
		membership.IsActive.Should().BeTrue();
		membership.CreatedAt.Should().Be(Now);
	}

	[Fact]
	public void Construtor_UserIdVazio_Lanca()
	{
		var act = () => new TenantMembership(Guid.Empty, Guid.NewGuid(), Now);

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Construtor_TenantIdVazio_Lanca()
	{
		var act = () => new TenantMembership(Guid.NewGuid(), Guid.Empty, Now);

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Suspend_VinculoAtivo_FicaInativo()
	{
		var membership = new TenantMembership(Guid.NewGuid(), Guid.NewGuid(), Now);

		membership.Suspend();

		membership.IsActive.Should().BeFalse();
	}

	[Fact]
	public void Reactivate_VinculoSuspenso_VoltaAtivo()
	{
		var membership = new TenantMembership(Guid.NewGuid(), Guid.NewGuid(), Now);
		membership.Suspend();

		membership.Reactivate();

		membership.IsActive.Should().BeTrue();
	}

	[Fact]
	public void SuspendEReactivate_Repetidos_SaoIdempotentes()
	{
		var membership = new TenantMembership(Guid.NewGuid(), Guid.NewGuid(), Now);

		membership.Suspend();
		membership.Suspend();
		membership.IsActive.Should().BeFalse();

		membership.Reactivate();
		membership.Reactivate();
		membership.IsActive.Should().BeTrue();
	}
}
