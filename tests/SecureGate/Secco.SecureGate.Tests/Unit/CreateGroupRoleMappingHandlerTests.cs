using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Cadastro do mapeamento grupo→perfil (issue #28, ADR-0036): federação é pré-requisito, grupo já
/// mapeado é conflito, perfil reservado nunca é mapeável.
/// </summary>
public class CreateGroupRoleMappingHandlerTests
{
	private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
	private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
	private readonly IGroupRoleMappingRepository _mappings = Substitute.For<IGroupRoleMappingRepository>();
	private readonly CreateGroupRoleMappingHandler _handler;

	public CreateGroupRoleMappingHandlerTests() => _handler = new CreateGroupRoleMappingHandler(_tenants, _roles, _mappings);

	private static CreateGroupRoleMappingCommand Command(Guid tenantId, Guid? groupId = null, string? role = "leitor") =>
		new(tenantId, groupId ?? Guid.NewGuid(), "Financeiro", role);

	[Fact]
	public async Task HandleAsync_SemGroupId_RetornaValidacao()
	{
		var result = await _handler.HandleAsync(Command(Guid.NewGuid(), Guid.Empty));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Federation.EntraGroupIdRequired);
	}

	[Fact]
	public async Task HandleAsync_PerfilReservado_RetornaRoleNotAssignable()
	{
		// ElevatedLogReaderRole (não o OperatorRole, que é o único reservado atribuível a usuários)
		var result = await _handler.HandleAsync(Command(Guid.NewGuid(), role: SecureGatePlatform.ElevatedLogReaderRole));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Users.RoleNotAssignable);
		await _tenants.DidNotReceiveWithAnyArgs().GetFederationAsync(default, default);
	}

	[Fact]
	public async Task HandleAsync_SemFederacaoCadastrada_RetornaNotEnabled()
	{
		var tenantId = Guid.NewGuid();
		_tenants.GetFederationAsync(tenantId, Arg.Any<CancellationToken>()).Returns((TenantFederation?)null);

		var result = await _handler.HandleAsync(Command(tenantId));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Federation.NotEnabled);
	}

	[Fact]
	public async Task HandleAsync_ComFederacaoDesabilitada_RetornaNotEnabled()
	{
		var tenantId = Guid.NewGuid();
		var federation = new TenantFederation(tenantId, Guid.NewGuid());
		federation.SetEnabled(false);
		_tenants.GetFederationAsync(tenantId, Arg.Any<CancellationToken>()).Returns(federation);

		var result = await _handler.HandleAsync(Command(tenantId));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Federation.NotEnabled);
	}

	[Fact]
	public async Task HandleAsync_GrupoJaMapeado_RetornaConflito()
	{
		var tenantId = Guid.NewGuid();
		var groupId = Guid.NewGuid();
		var federation = new TenantFederation(tenantId, Guid.NewGuid());
		_tenants.GetFederationAsync(tenantId, Arg.Any<CancellationToken>()).Returns(federation);
		_mappings.FindByGroupAsync(tenantId, groupId, Arg.Any<CancellationToken>())
			.Returns(new TenantGroupRoleMapping(tenantId, groupId, "Financeiro", Guid.NewGuid()));

		var result = await _handler.HandleAsync(Command(tenantId, groupId));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Federation.GroupAlreadyMapped);
	}

	[Fact]
	public async Task HandleAsync_PerfilInexistente_RetornaRoleNotFound()
	{
		var tenantId = Guid.NewGuid();
		var federation = new TenantFederation(tenantId, Guid.NewGuid());
		_tenants.GetFederationAsync(tenantId, Arg.Any<CancellationToken>()).Returns(federation);
		_mappings.FindByGroupAsync(tenantId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
			.Returns((TenantGroupRoleMapping?)null);
		_roles.FindRoleAsync(tenantId, "leitor", Arg.Any<CancellationToken>()).Returns((RoleSummaryData?)null);

		var result = await _handler.HandleAsync(Command(tenantId));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Roles.NotFound);
	}

	[Fact]
	public async Task HandleAsync_ComTudoValido_CriaOMapeamento()
	{
		var tenantId = Guid.NewGuid();
		var groupId = Guid.NewGuid();
		var roleId = Guid.NewGuid();
		var federation = new TenantFederation(tenantId, Guid.NewGuid());
		_tenants.GetFederationAsync(tenantId, Arg.Any<CancellationToken>()).Returns(federation);
		_mappings.FindByGroupAsync(tenantId, groupId, Arg.Any<CancellationToken>()).Returns((TenantGroupRoleMapping?)null);
		_roles.FindRoleAsync(tenantId, "leitor", Arg.Any<CancellationToken>())
			.Returns(new RoleSummaryData(roleId, "leitor", MemberCount: 0));

		var result = await _handler.HandleAsync(Command(tenantId, groupId));

		result.IsSuccess.Should().BeTrue();
		result.Value.EntraGroupId.Should().Be(groupId);
		result.Value.RoleId.Should().Be(roleId);
		result.Value.RoleName.Should().Be("leitor");
		await _mappings.Received(1).AddAsync(
			Arg.Is<TenantGroupRoleMapping>(m => m.TenantId == tenantId && m.EntraGroupId == groupId && m.RoleId == roleId),
			Arg.Any<CancellationToken>());
		await _mappings.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
	}
}
