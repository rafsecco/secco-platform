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
}
