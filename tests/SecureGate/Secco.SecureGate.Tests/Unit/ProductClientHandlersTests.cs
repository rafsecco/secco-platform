using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

public class ProductClientHandlersTests
{
	private static readonly Guid TenantId = Guid.NewGuid();

	private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
	private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
	private readonly IProductClientStore _store = Substitute.For<IProductClientStore>();

	public ProductClientHandlersTests()
	{
		_tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new Tenant("T", "t"));
		_roles.FindRoleAsync(TenantId, "compras-writer", Arg.Any<CancellationToken>())
			.Returns(new RoleSummaryData(Guid.NewGuid(), "compras-writer", 0));
		_store.CreateAsync(TenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ProductClientAccess>(), Arg.Any<CancellationToken>())
			.Returns(call => new ProductClientDto(
				call.ArgAt<string>(1), call.ArgAt<ProductClientAccess>(3).Name,
				call.ArgAt<ProductClientAccess>(3).Scopes, call.ArgAt<ProductClientAccess>(3).Roles, DateTimeOffset.UtcNow));
	}

	private CreateProductClientHandler Create() =>
		new(_tenants, new ProductClientAccessValidator(_roles), _store);

	[Fact]
	public async Task Create_Valido_DevolveSecretUmaVezEClientIdGerado()
	{
		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("Sistema de compras", ["logstream"], ["compras-writer"]));

		result.IsSuccess.Should().BeTrue();
		result.Value.ClientId.Should().StartWith("cli_");
		result.Value.ClientSecret.Should().HaveLength(43);
		result.Value.Roles.Should().Equal("compras-writer");
	}

	[Theory]
	[InlineData("securegate:admin")]
	[InlineData("authorization:read")]
	[InlineData("catalog:logstream")]
	[InlineData("securegate")]
	public async Task Create_EscopoDeInfraestrutura_Recusa(string scope)
	{
		var result = await Create().HandleAsync(TenantId, new ProductClientCommand("x", [scope], []));

		result.Error.Should().Be(SecureGateErrors.Clients.ScopeNotAllowed);
		await _store.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default!, default!, default);
	}

	[Fact]
	public async Task Create_PapelReservado_Recusa()
	{
		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("x", ["logstream"], [SecureGatePlatform.OperatorRole]));

		result.Error.Should().Be(SecureGateErrors.Clients.RoleNotAssignable);
	}

	[Fact]
	public async Task Create_PapelInexistente_Recusa()
	{
		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("x", ["logstream"], ["nao-existe"]));

		result.Error.Should().Be(SecureGateErrors.Clients.RoleNotFound);
	}

	[Fact]
	public async Task Create_NomeDuplicado_Conflito()
	{
		_store.NameExistsAsync(TenantId, "Sistema de compras", null, Arg.Any<CancellationToken>()).Returns(true);

		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("Sistema de compras", ["logstream"], []));

		result.Error.Should().Be(SecureGateErrors.Clients.NameAlreadyExists);
	}

	[Fact]
	public async Task Create_TenantInexistente_NotFound()
	{
		var result = await Create().HandleAsync(Guid.NewGuid(), new ProductClientCommand("x", ["logstream"], []));

		result.Error.Should().Be(SecureGateErrors.Tenants.NotFound);
	}

	[Fact]
	public async Task Create_TenantDesativado_Conflito()
	{
		var inactive = new Tenant("T", "t");
		inactive.Deactivate();
		_tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(inactive);

		var result = await Create().HandleAsync(TenantId, new ProductClientCommand("x", ["logstream"], []));

		result.Error.Should().Be(SecureGateErrors.Clients.TenantInactive);
	}

	[Fact]
	public async Task Update_MesmoNomeDoProprioClient_NaoConflita()
	{
		_store.NameExistsAsync(TenantId, "Sistema de compras", "cli_aaaaaaaaaaaaaaaa", Arg.Any<CancellationToken>())
			.Returns(false);
		_store.UpdateAsync(TenantId, "cli_aaaaaaaaaaaaaaaa", Arg.Any<ProductClientAccess>(), Arg.Any<CancellationToken>())
			.Returns(true);

		var result = await new UpdateProductClientHandler(new ProductClientAccessValidator(_roles), _store)
			.HandleAsync(TenantId, "cli_aaaaaaaaaaaaaaaa", new ProductClientCommand("Sistema de compras", ["logstream"], []));

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task Rotate_ClientInexistente_NotFound()
	{
		var result = await new RotateProductClientSecretHandler(_store).HandleAsync(TenantId, "cli_bbbbbbbbbbbbbbbb");

		result.Error.Should().Be(SecureGateErrors.Clients.NotFound);
	}

	// ADR-0052: gestão do próprio tenant por máquina — só o operador concede, altera, rotaciona ou exclui

	private const string ManagedClientId = "cli_mmmmmmmmmmmmmmmm";

	private void GivenExistingTenantManageClient() =>
		_store.GetAsync(TenantId, ManagedClientId, Arg.Any<CancellationToken>()).Returns(new ProductClientDto(
			ManagedClientId, "Intranet", [SecureGateScopes.TenantManage], [], DateTimeOffset.UtcNow,
			ProductClientRules.ClientCredentialsType));

	[Fact]
	public async Task Create_TenantManagePorAdminDeTenant_RecusaRequerOperador()
	{
		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("Intranet", [SecureGateScopes.TenantManage], []), byTenantAdmin: true);

		result.Error.Should().Be(SecureGateErrors.Clients.TenantManageRequiresOperator);
		await _store.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default!, default!, default);
	}

	[Fact]
	public async Task Create_TenantManagePeloOperador_Cria()
	{
		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("Intranet", [SecureGateScopes.TenantManage, SecureGateScopes.DirectoryRead], []));

		result.IsSuccess.Should().BeTrue();
		result.Value.Scopes.Should().Contain(SecureGateScopes.TenantManage);
	}

	[Fact]
	public async Task Create_TenantManageNoTenantDePlataforma_Recusa()
	{
		_tenants.GetByIdAsync(SecureGatePlatform.TenantId, Arg.Any<CancellationToken>()).Returns(new Tenant("I", "instalacao"));

		var result = await Create().HandleAsync(SecureGatePlatform.TenantId,
			new ProductClientCommand("Intranet", [SecureGateScopes.TenantManage], []));

		result.Error.Should().Be(SecureGateErrors.Clients.TenantManageNotOnPlatformTenant);
	}

	[Fact]
	public async Task Create_InterativoComTenantManage_Recusa()
	{
		var result = await Create().HandleAsync(TenantId, new ProductClientCommand(
			"Intranet", [SecureGateScopes.TenantManage], [], ProductClientRules.AuthorizationCodeType,
			["https://intranet.example.com/signin-oidc"]));

		result.Error.Should().Be(SecureGateErrors.Clients.ScopeNotAllowed);
	}

	[Fact]
	public async Task Update_ClientComTenantManagePorAdminDeTenant_Recusa()
	{
		GivenExistingTenantManageClient();

		var result = await new UpdateProductClientHandler(new ProductClientAccessValidator(_roles), _store)
			.HandleAsync(TenantId, ManagedClientId, new ProductClientCommand("Intranet", ["logstream"], []), byTenantAdmin: true);

		result.Error.Should().Be(SecureGateErrors.Clients.TenantManageRequiresOperator);
		await _store.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default!, default);
	}

	[Fact]
	public async Task Update_AcrescentandoTenantManagePorAdminDeTenant_Recusa()
	{
		var result = await new UpdateProductClientHandler(new ProductClientAccessValidator(_roles), _store)
			.HandleAsync(TenantId, "cli_aaaaaaaaaaaaaaaa",
				new ProductClientCommand("Sistema", [SecureGateScopes.TenantManage], []), byTenantAdmin: true);

		result.Error.Should().Be(SecureGateErrors.Clients.TenantManageRequiresOperator);
	}

	[Fact]
	public async Task Rotate_ClientComTenantManagePorAdminDeTenant_Recusa()
	{
		GivenExistingTenantManageClient();

		var result = await new RotateProductClientSecretHandler(_store).HandleAsync(TenantId, ManagedClientId, byTenantAdmin: true);

		result.Error.Should().Be(SecureGateErrors.Clients.TenantManageRequiresOperator);
		await _store.DidNotReceiveWithAnyArgs().RotateSecretAsync(default, default!, default!, default);
	}

	[Fact]
	public async Task Delete_ClientComTenantManagePorAdminDeTenant_Recusa()
	{
		GivenExistingTenantManageClient();

		var result = await new DeleteProductClientHandler(_store).HandleAsync(TenantId, ManagedClientId, byTenantAdmin: true);

		result.Error.Should().Be(SecureGateErrors.Clients.TenantManageRequiresOperator);
		await _store.DidNotReceiveWithAnyArgs().DeleteAsync(default, default!, default);
	}
}
