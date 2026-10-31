using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Authorization;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Permissions;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Perfil implícito do tenant (ADR-0056): o nome é reservado para criar, atribuir, mapear grupo e dar a client de
/// máquina, mas — diferente dos papéis de instalação — o admin edita as permissões dele. Excluir responde 409, a
/// lista de membros volta vazia, e todo caminho que cria tenant o cria junto.
/// </summary>
public class ImplicitTenantRoleTests
{
	private static readonly Guid AnyTenant = Guid.Parse("018f0000-0000-7000-8000-00000000abcd");

	private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();

	[Theory]
	[InlineData("tenant-member")]
	[InlineData("TENANT-MEMBER")]
	[InlineData("Tenant-Member")]
	public async Task CreateRole_ComONomeDoImplicito_RetornaNameReserved(string name)
	{
		var result = await new CreateRoleHandler(_roles).HandleAsync(new CreateRoleCommand(AnyTenant, name));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Roles.NameReserved);
		await _roles.DidNotReceiveWithAnyArgs().CreateRoleAsync(default, default!, default);
	}

	[Theory]
	[InlineData("tenant-member")]
	[InlineData("TENANT-MEMBER")]
	public void Implicito_NaoEhAtribuivelNemDeInstalacao(string name)
	{
		RoleInputRules.IsReservedName(name).Should().BeTrue();
		RoleInputRules.IsImplicitName(name).Should().BeTrue();
		RoleInputRules.IsInstallationName(name).Should().BeFalse("o implícito tem permissões editáveis");
		RoleInputRules.IsAssignableToUsers(name).Should().BeFalse();
	}

	[Fact]
	public async Task MapearGrupoParaOImplicito_RetornaRoleNotAssignable()
	{
		var handler = new CreateGroupRoleMappingHandler(
			Substitute.For<ITenantRepository>(), _roles, Substitute.For<IGroupRoleMappingRepository>());

		var result = await handler.HandleAsync(new CreateGroupRoleMappingCommand(
			AnyTenant, Guid.NewGuid(), "Todos", SecureGatePlatform.TenantMemberRole));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Users.RoleNotAssignable);
	}

	[Fact]
	public async Task SetPermissions_AceitaOImplicito()
	{
		var catalog = Substitute.For<IPermissionCatalogRepository>();
		catalog.FindUnknownAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns([]);
		_roles.GetPermissionsAsync(AnyTenant, SecureGatePlatform.TenantMemberRole, Arg.Any<CancellationToken>()).Returns([]);
		_roles.ReplacePermissionsAsync(AnyTenant, SecureGatePlatform.TenantMemberRole, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
			.Returns(true);

		var result = await new SetRolePermissionsHandler(_roles, catalog).HandleAsync(
			new SetRolePermissionsCommand(AnyTenant, SecureGatePlatform.TenantMemberRole, ["documentos:read"]));

		result.IsSuccess.Should().BeTrue();
		await _roles.Received(1).ReplacePermissionsAsync(
			AnyTenant, SecureGatePlatform.TenantMemberRole, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData(SecureGatePlatform.OperatorRole)]
	[InlineData(SecureGatePlatform.LegacyOperatorRole)]
	[InlineData(SecureGatePlatform.ElevatedLogReaderRole)]
	[InlineData(SecureGatePlatform.AuditorRole)]
	public async Task SetPermissions_SegueRecusandoOsDeInstalacao(string name)
	{
		var result = await new SetRolePermissionsHandler(_roles, Substitute.For<IPermissionCatalogRepository>()).HandleAsync(
			new SetRolePermissionsCommand(AnyTenant, name, ["documentos:read"]));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Roles.NameReserved);
		await _roles.DidNotReceiveWithAnyArgs().ReplacePermissionsAsync(default, default!, default!, default);
	}

	[Fact]
	public async Task DeleteRole_Implicito_Retorna409SemTocarNoBanco()
	{
		var result = await new DeleteRoleHandler(_roles).HandleAsync(AnyTenant, SecureGatePlatform.TenantMemberRole);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Roles.CannotDeleteImplicit);
		result.Error.Type.Should().Be(ErrorType.Conflict);
		await _roles.DidNotReceiveWithAnyArgs().DeleteRoleAsync(default, default!, default);
	}

	[Fact]
	public async Task ListRoleMembers_Implicito_VoltaVazioSemConsultarMembros()
	{
		_roles.FindRoleAsync(AnyTenant, SecureGatePlatform.TenantMemberRole, Arg.Any<CancellationToken>())
			.Returns(new RoleSummaryData(Guid.NewGuid(), SecureGatePlatform.TenantMemberRole, 0, IsImplicit: true));

		var result = await new ListRoleMembersHandler(_roles).HandleAsync(
			AnyTenant, SecureGatePlatform.TenantMemberRole, PageRequest.Default);

		result.IsSuccess.Should().BeTrue();
		result.Value.Items.Should().BeEmpty();
		result.Value.TotalCount.Should().Be(0);
		await _roles.DidNotReceiveWithAnyArgs().ListMembersAsync(default, default!, default);
	}

	[Fact]
	public async Task GetRole_Implicito_MarcaImplicitoENaoReservado()
	{
		_roles.FindRoleAsync(AnyTenant, SecureGatePlatform.TenantMemberRole, Arg.Any<CancellationToken>())
			.Returns(new RoleSummaryData(Guid.NewGuid(), SecureGatePlatform.TenantMemberRole, 0, IsImplicit: true));
		_roles.GetPermissionsAsync(AnyTenant, SecureGatePlatform.TenantMemberRole, Arg.Any<CancellationToken>())
			.Returns(["documentos:read"]);
		var catalog = Substitute.For<IPermissionCatalogRepository>();
		catalog.FindUnknownAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>()).Returns([]);

		var result = await new GetRoleHandler(_roles, new GetRolePermissionsHandler(_roles), catalog)
			.HandleAsync(AnyTenant, SecureGatePlatform.TenantMemberRole);

		result.IsSuccess.Should().BeTrue();
		result.Value.IsImplicit.Should().BeTrue();
		result.Value.IsReserved.Should().BeFalse("o admin edita o implícito");
		result.Value.Permissions.Should().Equal("documentos:read");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CreateTenant_EmQualquerCaminho_EnfileiraOImplicitoAntesDaGravacao(bool createdByPerson)
	{
		var tenants = Substitute.For<ITenantRepository>();
		var grants = Substitute.For<ITenantAdminGrantRepository>();
		var handler = new CreateTenantHandler(
			tenants, _roles, grants, Substitute.For<ITenantAccessScope>(), new TenantAdminOptions());
		var order = new List<string>();
		tenants.When(t => t.AddAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>())).Do(_ => order.Add("tenant"));
		_roles.When(r => r.StageImplicitRole(Arg.Any<Guid>())).Do(_ => order.Add("implicito"));
		grants.When(g => g.AddAsync(Arg.Any<TenantAdminGrant>(), Arg.Any<CancellationToken>())).Do(_ => order.Add("gravacao"));
		tenants.When(t => t.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => order.Add("gravacao"));

		var result = await handler.HandleAsync(new CreateTenantCommand(
			"Novo", "novo-" + Guid.NewGuid().ToString("N")[..12], createdByPerson ? Guid.NewGuid() : null));

		result.IsSuccess.Should().BeTrue();
		_roles.Received(1).StageImplicitRole(result.Value.Id);
		order.Should().Equal("tenant", "implicito", "gravacao");
	}

	[Fact]
	public void TokensSemTenant_NaoLevamOImplicito()
	{
		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = SecureGatePlatform.TenantId,
			UserName = "op@secco.test",
			Email = "op@secco.test",
		};

		var elevation = OidcPrincipalBuilder.ForElevation(user, [], TimeSpan.FromMinutes(15));
		var designated = OidcPrincipalBuilder.ForDesignatedOperator(user, [], TimeSpan.FromMinutes(15));

		elevation.FindAll("role").Select(c => c.Value).Should().Equal(SecureGatePlatform.ElevatedLogReaderRole);
		designated.FindAll("role").Select(c => c.Value).Should().NotContain(SecureGatePlatform.TenantMemberRole);
	}
}
