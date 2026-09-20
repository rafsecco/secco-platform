using FluentAssertions;
using NSubstitute;
using Secco.AdminPortal.Services;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Serviços de gestão de identidade (Fase 7.2): orquestram o <c>Secco.SecureGate.Client</c>
/// e projetam os DTOs gerados para os modelos da UI. Testados com o client substituído
/// (a fábrica devolve o mock) — a autenticação já é coberta em <c>SecureGateClientFactoryTests</c>.
/// </summary>
public class IdentityAdminServicesTests
{
	private static (ISecureGateClientFactory Factory, ISecureGateClient Client) BuildFactory()
	{
		var client = Substitute.For<ISecureGateClient>();
		var factory = Substitute.For<ISecureGateClientFactory>();
		factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(client);

		return (factory, client);
	}

	[Fact]
	public async Task ListUsers_ProjectsDtoToSummary()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.ListUsersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<UserDto>
		{
			new() { Id = Guid.NewGuid(), Email = "ana@acme.test", TenantId = tenantId, Roles = ["leitor", "operador"] },
		});

		var users = await new SecureGateUserAdminService(factory).ListUsersAsync(tenantId);

		users.Should().ContainSingle();
		users[0].Email.Should().Be("ana@acme.test");
		users[0].Roles.Should().BeEquivalentTo("leitor", "operador");
	}

	[Fact]
	public async Task CreateUser_SendsEmailLocalLoginAndRoles()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();

		await new SecureGateUserAdminService(factory)
			.CreateUserAsync(tenantId, "novo@acme.test", localLogin: true, ["leitor"]);

		// Nenhuma senha sai do portal (ADR-0033): o operador escolhe o MODO de acesso, e quem
		// define a credencial é a própria pessoa, pelo convite.
		await client.Received(1).CreateUserAsync(
			tenantId,
			Arg.Is<CreateUserRequest>(r => r.Email == "novo@acme.test" && r.LocalLogin == true && r.Roles.Contains("leitor")),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task CreateUser_SoLoginCorporativo_NaoPedeConvite()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();

		await new SecureGateUserAdminService(factory)
			.CreateUserAsync(tenantId, "terceiro@acme.test", localLogin: false, []);

		await client.Received(1).CreateUserAsync(
			tenantId,
			Arg.Is<CreateUserRequest>(r => r.LocalLogin == false),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task ListRoles_ProjectsPermissions()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.ListRolesAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<RoleDto>
		{
			new() { Name = "operador", Permissions = ["log-entries:read", "log-entries:write"] },
		});

		var roles = await new SecureGateRoleAdminService(factory).ListRolesAsync(tenantId);

		roles.Should().ContainSingle();
		roles[0].Name.Should().Be("operador");
		roles[0].Permissions.Should().BeEquivalentTo("log-entries:read", "log-entries:write");
	}

	[Fact]
	public async Task SetPermissions_SendsTheFullSet()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();

		await new SecureGateRoleAdminService(factory)
			.SetPermissionsAsync(tenantId, "operador", ["log-entries:read", "api-call-logs:read"]);

		await client.Received(1).SetRolePermissionsAsync(
			tenantId,
			"operador",
			Arg.Is<SetRolePermissionsRequest>(r => r.Permissions.Count == 2 && r.Permissions.Contains("api-call-logs:read")),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task UpsertDatabase_SendsConnectionStringWriteOnly()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();

		await new SecureGateTenantAdminService(factory)
			.UpsertDatabaseAsync(tenantId, "logstream", "Server=segredo;Database=x;");

		await client.Received(1).UpsertTenantDatabaseAsync(
			tenantId,
			"logstream",
			Arg.Is<UpsertTenantDatabaseRequest>(r => r.ConnectionString == "Server=segredo;Database=x;"),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetTenant_ProjectsDetail()
	{
		var (factory, client) = BuildFactory();
		var id = Guid.NewGuid();
		client.GetTenantAsync(id, Arg.Any<CancellationToken>()).Returns(new TenantDetailDto
		{
			Id = id,
			Name = "Acme",
			Slug = "acme",
			IsActive = true,
			CreatedAt = DateTimeOffset.UtcNow,
			Products = ["logstream"],
		});

		var detail = await new SecureGateTenantAdminService(factory).GetTenantAsync(id);

		detail.Name.Should().Be("Acme");
		detail.IsActive.Should().BeTrue();
		detail.Products.Should().ContainSingle().Which.Should().Be("logstream");
	}

	[Fact]
	public async Task ListUsers_ProjetaSituacao()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.ListUsersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<UserDto>
		{
			new() { Id = Guid.NewGuid(), Email = "ana@acme.test", TenantId = tenantId, Roles = [], Status = "Deactivated" },
		});

		var users = await new SecureGateUserAdminService(factory).ListUsersAsync(tenantId);

		users[0].Status.Should().Be("Deactivated");
	}

	[Fact]
	public async Task GetUser_ProjetaDetalhe()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		client.GetUserAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns(new UserDetailDto
		{
			Id = userId,
			Email = "ana@acme.test",
			TenantId = tenantId,
			Status = "Active",
			Roles = ["leitor"],
			EffectivePermissions = ["documentos:read"],
			ExternalLogins = ["EntraId"],
			HasPassword = true,
			LocalLoginEnabled = true,
		});

		var user = await new SecureGateUserAdminService(factory).GetUserAsync(tenantId, userId);

		user.Email.Should().Be("ana@acme.test");
		user.Roles.Should().Equal("leitor");
		user.EffectivePermissions.Should().Equal("documentos:read");
		user.ExternalLogins.Should().Equal("EntraId");

		// É por estes dois campos que a tela decide entre "reenviar convite", "redefinir senha"
		// e nenhum dos dois (ADR-0033).
		user.HasPassword.Should().BeTrue();
		user.LocalLoginEnabled.Should().BeTrue();
	}

	[Fact]
	public async Task GetUser_ContaSoCorporativa_ProjetaOsDoisCamposDesligados()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		client.GetUserAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns(new UserDetailDto
		{
			Id = userId,
			Email = "terceiro@acme.test",
			TenantId = tenantId,
			Status = "Active",
			Roles = [],
			EffectivePermissions = [],
			ExternalLogins = ["EntraId"],
			HasPassword = false,
			LocalLoginEnabled = false,
		});

		var user = await new SecureGateUserAdminService(factory).GetUserAsync(tenantId, userId);

		user.HasPassword.Should().BeFalse();
		user.LocalLoginEnabled.Should().BeFalse();
	}

	[Fact]
	public async Task ReenviarConvite_ChamaOClient()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();

		await new SecureGateUserAdminService(factory).ResendInviteAsync(tenantId, userId);

		await client.Received(1).ResendUserInviteAsync(tenantId, userId, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task RedefinirSenha_ChamaOClientSemNenhumaSenha()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();

		await new SecureGateUserAdminService(factory).ResetPasswordAsync(tenantId, userId);

		// A operação manda um LINK; nenhuma senha passa pelo portal (ADR-0033).
		await client.Received(1).ResetUserPasswordAsync(tenantId, userId, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task LigarEDesligarLoginLocal_EnviamOEstadoPedido()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		var service = new SecureGateUserAdminService(factory);

		await service.SetLocalLoginAsync(tenantId, userId, enabled: false);
		await service.SetLocalLoginAsync(tenantId, userId, enabled: true);

		await client.Received(1).SetUserLocalLoginAsync(
			tenantId, userId, Arg.Is<SetLocalLoginRequest>(r => !r.Enabled), Arg.Any<CancellationToken>());
		await client.Received(1).SetUserLocalLoginAsync(
			tenantId, userId, Arg.Is<SetLocalLoginRequest>(r => r.Enabled), Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task AddERemoveRole_ChamamOClient()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		var service = new SecureGateUserAdminService(factory);

		await service.AddRoleAsync(tenantId, userId, "leitor");
		await service.RemoveRoleAsync(tenantId, userId, "leitor");

		await client.Received(1).AddUserRoleAsync(tenantId, userId, "leitor", Arg.Any<CancellationToken>());
		await client.Received(1).RemoveUserRoleAsync(tenantId, userId, "leitor", Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SetActive_EscolheAOperacao()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		var service = new SecureGateUserAdminService(factory);

		await service.SetActiveAsync(tenantId, userId, active: false);
		await service.SetActiveAsync(tenantId, userId, active: true);

		await client.Received(1).DeactivateUserAsync(tenantId, userId, Arg.Any<CancellationToken>());
		await client.Received(1).ActivateUserAsync(tenantId, userId, Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetRole_ProjetaDetalhe()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.GetRoleAsync(tenantId, "leitor", Arg.Any<CancellationToken>()).Returns(new RoleDetailDto
		{
			Name = "leitor",
			Permissions = ["documentos:read"],
			IsReserved = false,
			MemberCount = 3,
		});

		var role = await new SecureGateRoleAdminService(factory).GetRoleAsync(tenantId, "leitor");

		role.Should().BeEquivalentTo(new RoleDetail("leitor", ["documentos:read"], false, 3));
	}

	[Fact]
	public async Task ListMembers_PedeAPaginaComTamanhoFixo()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var memberId = Guid.NewGuid();
		client.ListRoleMembersAsync(tenantId, "leitor", 2, IRoleAdminService.MembersPageSize, Arg.Any<CancellationToken>())
			.Returns(new PagedResultOfRoleMemberDto
			{
				Items = [new RoleMemberDto { UserId = memberId, Email = "ana@acme.test", Status = "Active" }],
				Page = 2,
				Size = IRoleAdminService.MembersPageSize,
				TotalCount = 21,
				TotalPages = 2,
			});

		var page = await new SecureGateRoleAdminService(factory).ListMembersAsync(tenantId, "leitor", 2);

		page.Page.Should().Be(2);
		page.TotalPages.Should().Be(2);
		page.Items.Should().ContainSingle(m => m.UserId == memberId && m.Status == "Active");
	}

	[Fact]
	public async Task DeleteRole_ChamaOClient()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();

		await new SecureGateRoleAdminService(factory).DeleteRoleAsync(tenantId, "leitor");

		await client.Received(1).DeleteRoleAsync(tenantId, "leitor", Arg.Any<CancellationToken>());
	}

	[Theory]
	[InlineData("Active", "Ativo")]
	[InlineData("Deactivated", "Desativado")]
	[InlineData("LockedOut", "Bloqueado")]
	[InlineData("Outro", "Outro")]
	public void UserStatusText_Traduz(string status, string expected) =>
		UserStatusText.Describe(status).Should().Be(expected);

	[Fact]
	public void ApiErrorFormatter_Conflito_MostraODetalheDoServidor() =>
		ApiErrorFormatter.Describe(409, """{"title":"Conflict","detail":"A operação deixaria a instalação sem nenhum operador ativo."}""")
			.Should().Be("A operação deixaria a instalação sem nenhum operador ativo.");
}
