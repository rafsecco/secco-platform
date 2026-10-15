using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Members;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Regras dos handlers de vínculo (ADR-0040) que a API de integração não alcança com o token de teste
/// (o <c>sub</c> de quem chama não é uma pessoa) ou que são mais claras isoladas.
/// </summary>
public class TenantMemberHandlersTests
{
	private static readonly Guid Tenant = Guid.NewGuid();
	private static readonly Guid Owner = Guid.NewGuid();
	private static readonly Guid User = Guid.NewGuid();

	private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
	private readonly IUserDirectory _directory = Substitute.For<IUserDirectory>();
	private readonly ITenantMembershipRepository _memberships = Substitute.For<ITenantMembershipRepository>();
	private readonly ISessionRevoker _revoker = Substitute.For<ISessionRevoker>();

	public TenantMemberHandlersTests()
	{
		_tenants.GetByIdAsync(Tenant, Arg.Any<CancellationToken>()).Returns(new Tenant("Tenant", "tenant-unit"));
		_directory.GetOwnerTenantAsync(User, Arg.Any<CancellationToken>()).Returns(Owner);
	}

	[Fact]
	public async Task Suspend_ChamadorEAPropriaPessoa_Retorna409SemTocarNoVinculo()
	{
		var handler = new SuspendTenantMemberHandler(_tenants, _directory, _memberships, _revoker);

		var result = await handler.HandleAsync(new SuspendTenantMemberCommand(Tenant, User, User.ToString().ToUpperInvariant()));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Members.CannotChangeOwnMembership);
		await _memberships.DidNotReceiveWithAnyArgs().SaveChangesAsync();
		await _revoker.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default);
	}

	[Fact]
	public async Task Remove_ChamadorEAPropriaPessoa_Retorna409SemRemover()
	{
		var handler = new RemoveTenantMemberHandler(_tenants, _directory, _memberships, _revoker);

		var result = await handler.HandleAsync(new RemoveTenantMemberCommand(Tenant, User, User.ToString()));

		result.Error.Should().Be(SecureGateErrors.Members.CannotChangeOwnMembership);
		await _memberships.DidNotReceiveWithAnyArgs().RemoveAsync(default, default);
		await _revoker.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default);
	}

	[Fact]
	public async Task Suspend_OutroChamador_SuspendeERevogaComOMotivoDoVinculo()
	{
		var membership = new TenantMembership(User, Tenant, DateTimeOffset.UtcNow);
		_memberships.GetAsync(Tenant, User, Arg.Any<CancellationToken>()).Returns(membership);
		var handler = new SuspendTenantMemberHandler(_tenants, _directory, _memberships, _revoker);

		var result = await handler.HandleAsync(new SuspendTenantMemberCommand(Tenant, User, Guid.NewGuid().ToString()));

		result.IsSuccess.Should().BeTrue();
		membership.IsActive.Should().BeFalse();
		await _revoker.Received(1).RevokeAllAsync(User, SessionRevocationReason.MembershipEnded, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Remove_SemVinculo_Retorna204SemRevogar()
	{
		_memberships.RemoveAsync(Tenant, User, Arg.Any<CancellationToken>()).Returns(false);
		var handler = new RemoveTenantMemberHandler(_tenants, _directory, _memberships, _revoker);

		var result = await handler.HandleAsync(new RemoveTenantMemberCommand(Tenant, User, null));

		result.IsSuccess.Should().BeTrue();
		await _revoker.DidNotReceiveWithAnyArgs().RevokeAllAsync(default, default);
	}

	[Fact]
	public async Task Remove_ComVinculo_RevogaUmaVez()
	{
		_memberships.RemoveAsync(Tenant, User, Arg.Any<CancellationToken>()).Returns(true);
		var handler = new RemoveTenantMemberHandler(_tenants, _directory, _memberships, _revoker);

		(await handler.HandleAsync(new RemoveTenantMemberCommand(Tenant, User, null))).IsSuccess.Should().BeTrue();

		await _revoker.Received(1).RevokeAllAsync(User, SessionRevocationReason.MembershipEnded, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Set_PerdeACorridaDeInclusao_ReativaOVinculoSuspensoQueOUtroCriou()
	{
		var membership = new TenantMembership(User, Tenant, DateTimeOffset.UtcNow);
		membership.Suspend();
		_memberships.GetAsync(Tenant, User, Arg.Any<CancellationToken>()).Returns(null, membership);
		_memberships.TryAddAsync(Arg.Any<TenantMembership>(), Arg.Any<CancellationToken>()).Returns(false);
		var handler = new SetTenantMemberHandler(_tenants, _directory, _memberships);

		var result = await handler.HandleAsync(Tenant, User);

		result.IsSuccess.Should().BeTrue();
		membership.IsActive.Should().BeTrue();
	}

	[Fact]
	public async Task Add_DuplicataDaCorrida_Retorna409()
	{
		_directory.FindPersonByEmailAsync("a@secco.test", Arg.Any<CancellationToken>())
			.Returns(new PersonData(User, "a@secco.test", null, Owner));
		_memberships.TryAddAsync(Arg.Any<TenantMembership>(), Arg.Any<CancellationToken>()).Returns(false);
		var handler = new AddTenantMemberHandler(_tenants, _directory, _memberships);

		var result = await handler.HandleAsync(Tenant, "a@secco.test");

		result.Error.Should().Be(SecureGateErrors.Members.AlreadyMember);
	}

	[Fact]
	public async Task Add_EmailComEspacos_ProcuraPeloValorAparado()
	{
		_directory.FindPersonByEmailAsync("a@secco.test", Arg.Any<CancellationToken>())
			.Returns(new PersonData(User, "a@secco.test", "Ana", Owner));
		_memberships.TryAddAsync(Arg.Any<TenantMembership>(), Arg.Any<CancellationToken>()).Returns(true);
		var handler = new AddTenantMemberHandler(_tenants, _directory, _memberships);

		var result = await handler.HandleAsync(Tenant, "  a@secco.test  ");

		result.IsSuccess.Should().BeTrue();
		result.Value.DisplayName.Should().Be("Ana");
		result.Value.OwnerTenantId.Should().Be(Owner);
	}
}
