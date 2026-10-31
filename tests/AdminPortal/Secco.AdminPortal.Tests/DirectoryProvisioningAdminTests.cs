using FluentAssertions;
using NSubstitute;
using Secco.AdminPortal.Services;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Provisionamento pelo diretório no console (ADR-0055, entrega B): marcar e desmarcar o grupo de provisionamento,
/// domínios de e-mail da federação e a indicação "gerida pelo diretório" nas listas de usuários. Testados com o client
/// substituído, como os demais serviços de gestão.
/// </summary>
public class DirectoryProvisioningAdminTests
{
	private static readonly string[] TwoDomains = ["empresa.com", "empresa.com.br"];

	private static (ISecureGateClientFactory Factory, ISecureGateClient Client) BuildFactory()
	{
		var client = Substitute.For<ISecureGateClient>();
		var factory = Substitute.For<ISecureGateClientFactory>();
		factory.CreateAsync(Arg.Any<CancellationToken>()).Returns(client);

		return (factory, client);
	}

	// ---- Grupo de provisionamento ----

	[Fact]
	public async Task SetProvisioningGroup_Marcar_ChamaOPutDaRotaDoMapeamento()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var mappingId = Guid.NewGuid();

		await new SecureGateTenantAdminService(factory).SetProvisioningGroupAsync(tenantId, mappingId, provisioning: true);

		await client.Received(1).MarkProvisioningGroupAsync(tenantId, mappingId, Arg.Any<CancellationToken>());
		await client.DidNotReceiveWithAnyArgs().UnmarkProvisioningGroupAsync(default, default, default);
	}

	[Fact]
	public async Task SetProvisioningGroup_Desmarcar_ChamaODeleteDaRotaDoMapeamento()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var mappingId = Guid.NewGuid();

		await new SecureGateTenantAdminService(factory).SetProvisioningGroupAsync(tenantId, mappingId, provisioning: false);

		await client.Received(1).UnmarkProvisioningGroupAsync(tenantId, mappingId, Arg.Any<CancellationToken>());
		await client.DidNotReceiveWithAnyArgs().MarkProvisioningGroupAsync(default, default, default);
	}

	[Fact]
	public async Task ListGroupMappings_ProjetaAMarcaDeProvisionamento()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var marked = Guid.NewGuid();
		client.ListGroupRoleMappingsAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<GroupRoleMappingRecord>
		{
			new() { Id = marked, EntraGroupId = Guid.NewGuid(), EntraGroupDisplayName = "Todos", RoleName = "base", IsProvisioningGroup = true },
			new() { Id = Guid.NewGuid(), EntraGroupId = Guid.NewGuid(), EntraGroupDisplayName = "Financeiro", RoleName = "financeiro" },
		});

		var mappings = await new SecureGateTenantAdminService(factory).ListGroupMappingsAsync(tenantId);

		mappings.Should().HaveCount(2);
		mappings.Single(m => m.Id == marked).Should().BeEquivalentTo(new { GroupName = "Todos", RoleName = "base", IsProvisioningGroup = true });
		mappings.Single(m => m.GroupName == "Financeiro").IsProvisioningGroup.Should().BeFalse();
	}

	// ---- Domínios permitidos ----

	[Fact]
	public async Task SetFederation_EnviaOsDominiosInformados()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var directoryId = Guid.NewGuid();

		await new SecureGateTenantAdminService(factory)
			.SetFederationAsync(tenantId, directoryId, enabled: true, allowedEmailDomains: ["empresa.com", "empresa.com.br"]);

		await client.Received(1).UpsertTenantFederationAsync(
			tenantId,
			Arg.Is<UpsertTenantFederationRequest>(r =>
				r.DirectoryId == directoryId && r.Enabled == true
				&& r.AllowedEmailDomains.SequenceEqual(TwoDomains)),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task SetFederation_SemDominios_NaoMandaALista_ParaOSecureGateManterAAtual()
	{
		var (factory, client) = BuildFactory();

		await new SecureGateTenantAdminService(factory).SetFederationAsync(Guid.NewGuid(), Guid.NewGuid(), enabled: false);

		await client.Received(1).UpsertTenantFederationAsync(
			Arg.Any<Guid>(),
			Arg.Is<UpsertTenantFederationRequest>(r => r.AllowedEmailDomains == null),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task GetTenant_ProjetaOsDominiosDaFederacao()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.GetTenantAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new TenantDetailDto
		{
			Id = tenantId,
			Name = "Acme",
			Slug = "acme",
			IsActive = true,
			Products = [],
			Federation = new TenantFederationDto
			{
				Provider = "entra-id",
				DirectoryId = Guid.NewGuid(),
				IsEnabled = true,
				AllowedEmailDomains = ["acme.com"],
			},
		});

		var tenant = await new SecureGateTenantAdminService(factory).GetTenantAsync(tenantId);

		tenant.Federation!.AllowedEmailDomains.Should().Equal("acme.com");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void DomainsInput_EmBranco_MantemALista(string? input)
	{
		FederationDomainsInput.Parse(input).Should().BeNull();
	}

	[Fact]
	public void DomainsInput_Hifen_EsvaziaALista()
	{
		FederationDomainsInput.Parse(" - ").Should().NotBeNull().And.BeEmpty();
	}

	[Theory]
	[InlineData("empresa.com", new[] { "empresa.com" })]
	[InlineData("empresa.com, empresa.com.br", new[] { "empresa.com", "empresa.com.br" })]
	[InlineData("a.com;b.com   c.com\nd.com", new[] { "a.com", "b.com", "c.com", "d.com" })]
	[InlineData("a.com, A.COM", new[] { "a.com" })]
	public void DomainsInput_SeparaPorVirgulaPontoEVirgulaOuEspaco_SemRepetir(string input, string[] expected)
	{
		FederationDomainsInput.Parse(input).Should().Equal(expected);
	}

	// ---- Usuários ----

	[Fact]
	public async Task ListUsers_ProjetaContaGeridaEOrigemDaDesativacao()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		client.ListUsersAsync(tenantId, Arg.Any<CancellationToken>()).Returns(new List<UserDto>
		{
			new()
			{
				Id = Guid.NewGuid(), Email = "gerida@acme.test", TenantId = tenantId, Roles = [], Status = "Deactivated",
				DirectoryManaged = true, DeactivationOrigin = "Directory",
			},
			new() { Id = Guid.NewGuid(), Email = "local@acme.test", TenantId = tenantId, Roles = [], Status = "Active" },
		});

		var users = await new SecureGateUserAdminService(factory).ListUsersAsync(tenantId);

		users.Single(u => u.Email == "gerida@acme.test").Should().BeEquivalentTo(
			new { DirectoryManaged = true, DeactivationOrigin = DirectoryText.DirectoryOrigin });
		users.Single(u => u.Email == "local@acme.test").DirectoryManaged.Should().BeFalse();
	}

	[Fact]
	public async Task GetUser_ProjetaContaGeridaEOrigemDaDesativacao()
	{
		var (factory, client) = BuildFactory();
		var tenantId = Guid.NewGuid();
		var userId = Guid.NewGuid();
		client.GetUserAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns(new UserDetailDto
		{
			Id = userId,
			Email = "gerida@acme.test",
			TenantId = tenantId,
			Status = "Deactivated",
			Roles = [],
			EffectivePermissions = [],
			ExternalLogins = ["EntraId"],
			DirectoryManaged = true,
			DeactivationOrigin = "Manual",
		});

		var user = await new SecureGateUserAdminService(factory).GetUserAsync(tenantId, userId);

		user.DirectoryManaged.Should().BeTrue();
		user.DeactivationOrigin.Should().Be(DirectoryText.ManualOrigin);
	}

	[Theory]
	[InlineData(false, "None", "—")]
	[InlineData(false, "Directory", "—")]
	[InlineData(true, "None", "Gerida pelo diretório")]
	[InlineData(true, "Directory", "Gerida pelo diretório — desativada pelo diretório")]
	[InlineData(true, "Manual", "Gerida pelo diretório — desativada manualmente")]
	public void DirectoryText_Describe_DizSeAContaEGeridaEQuemADesativou(bool managed, string origin, string expected)
	{
		DirectoryText.Describe(managed, origin).Should().Be(expected);
	}

	[Fact]
	public void DirectoryText_DescribeOrigin_ExplicaQuemDesativouEOQueAcontece()
	{
		DirectoryText.DescribeOrigin(DirectoryText.DirectoryOrigin).Should().Contain("Volta sozinha");
		DirectoryText.DescribeOrigin(DirectoryText.ManualOrigin).Should().Contain("não a reativa");
		DirectoryText.DescribeOrigin(DirectoryText.NoOrigin).Should().BeNull();
	}
}
