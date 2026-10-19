using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>Client interativo (ADR-0040): regra de redirect URI, política de Development e imutabilidade do tipo.</summary>
public class ProductClientInteractiveValidationTests
{
	private static readonly Guid TenantId = Guid.NewGuid();
	private const string AuthorizationCode = ProductClientRules.AuthorizationCodeType;

	private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
	private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
	private readonly IProductClientStore _store = Substitute.For<IProductClientStore>();

	[Theory]
	[InlineData("https://app.exemplo.com/cb", false, true)]
	[InlineData("http://app.exemplo.com/cb", false, false)]
	[InlineData("http://localhost:5001/signin", true, true)]
	[InlineData("https://app.exemplo.com/cb#x", true, false)]
	[InlineData("/relativa", true, false)]
	[InlineData("ftp://app.exemplo.com/cb", true, false)]
	[InlineData("javascript:alert(1)", true, false)]
	[InlineData("https://u:p@app.exemplo.com/cb", true, false)]
	[InlineData("", true, false)]
	[InlineData(null, true, false)]
	public void IsValidRedirectUri_AplicaARegraUnica(string? uri, bool allowInsecure, bool expected) =>
		ProductClientRules.IsValidRedirectUri(uri, allowInsecure).Should().Be(expected);

	[Fact]
	public void IsValidRedirectUri_UriGigante_Recusada() =>
		ProductClientRules.IsValidRedirectUri("https://a.com/" + new string('x', ProductClientRules.RedirectUriMaxLength), false)
			.Should().BeFalse();

	[Fact]
	public async Task Validate_HttpEmDevelopment_Aceito_ForaDelaRecusado()
	{
		var command = new ProductClientCommand("App", ["logstream"], [], AuthorizationCode, ["http://localhost:5001/cb"]);

		var development = await new ProductClientAccessValidator(_roles, new FixedPolicy(true)).ValidateAsync(TenantId, command);
		var production = await new ProductClientAccessValidator(_roles, new FixedPolicy(false)).ValidateAsync(TenantId, command);
		var noPolicy = await new ProductClientAccessValidator(_roles).ValidateAsync(TenantId, command);

		development.IsSuccess.Should().BeTrue();
		production.Error.Should().Be(SecureGateErrors.Clients.RedirectUrisInvalid);
		noPolicy.Error.Should().Be(SecureGateErrors.Clients.RedirectUrisInvalid, "sem política, o padrão é o mais restrito");
	}

	[Fact]
	public async Task Create_InterativoNoTenantDePlataforma_Conflito()
	{
		_tenants.GetByIdAsync(SecureGatePlatform.TenantId, Arg.Any<CancellationToken>())
			.Returns(new Tenant("Instalação", "instalacao"));
		var handler = new CreateProductClientHandler(_tenants, new ProductClientAccessValidator(_roles), _store);

		var result = await handler.HandleAsync(
			SecureGatePlatform.TenantId,
			new ProductClientCommand("App", ["logstream"], [], AuthorizationCode, ["https://app.exemplo.com/cb"]));

		result.Error.Should().Be(SecureGateErrors.Clients.InteractiveInPlatformTenant);
		await _store.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default!, default!, default);
	}

	[Fact]
	public async Task Update_TipoDiferenteDoGravado_TypeImmutable()
	{
		_store.GetAsync(TenantId, "cli_aaaaaaaaaaaaaaaa", Arg.Any<CancellationToken>()).Returns(
			new ProductClientDto("cli_aaaaaaaaaaaaaaaa", "App", ["logstream"], [], null, AuthorizationCode, ["https://app.exemplo.com/cb"]));
		var handler = new UpdateProductClientHandler(new ProductClientAccessValidator(_roles), _store);

		// tipo omitido = client_credentials ≠ authorization_code gravado
		var result = await handler.HandleAsync(TenantId, "cli_aaaaaaaaaaaaaaaa", new ProductClientCommand("App", ["logstream"], []));

		result.Error.Should().Be(SecureGateErrors.Clients.TypeImmutable);
		await _store.DidNotReceiveWithAnyArgs().UpdateAsync(default, default!, default!, default);
	}

	[Fact]
	public async Task Validate_InterativoComPapeis_Recusado()
	{
		var command = new ProductClientCommand("App", ["logstream"], ["algum"], AuthorizationCode, ["https://app.exemplo.com/cb"]);

		var result = await new ProductClientAccessValidator(_roles).ValidateAsync(TenantId, command);

		result.Error.Should().Be(SecureGateErrors.Clients.RolesNotAllowedForInteractive);
	}

	[Fact]
	public async Task Validate_InterativoComEscopoDeDiretorio_ScopeNotAllowed()
	{
		var command = new ProductClientCommand("App", [SecureGateScopes.DirectoryRead], [], AuthorizationCode, ["https://app.exemplo.com/cb"]);

		var result = await new ProductClientAccessValidator(_roles).ValidateAsync(TenantId, command);

		result.Error.Should().Be(SecureGateErrors.Clients.ScopeNotAllowed);
	}

	[Fact]
	public async Task Validate_ClientCredentialsComEscopoDeDiretorio_Aceito()
	{
		var command = new ProductClientCommand("Maquina", [SecureGateScopes.DirectoryRead], []);

		var result = await new ProductClientAccessValidator(_roles).ValidateAsync(TenantId, command);

		result.IsSuccess.Should().BeTrue();
	}

	private sealed class FixedPolicy(bool allowInsecure) : IRedirectUriPolicy
	{
		public bool AllowInsecure { get; } = allowInsecure;
	}
}
