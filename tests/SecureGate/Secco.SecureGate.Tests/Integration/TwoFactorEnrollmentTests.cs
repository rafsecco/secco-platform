using System.Globalization;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Cadastro do segundo fator (entrega D): estado inicial, geração da chave com QR embutido
/// gerado no próprio servidor, confirmação obrigatória antes de ligar e os códigos de
/// recuperação.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class TwoFactorEnrollmentTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private const string ClientId = "dois-fatores-e2e";
	private const string RedirectUri = "https://localhost/callback";

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await factory.CreatePublicClientAsync(ClientId, RedirectUri, "logstream");
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"dois-fatores-{Guid.NewGuid():N}@secco.test";

	private async Task<Guid> UserAsync() => await IdentitySeed.UserAsync(factory, _tenantId, Email());

	[Fact]
	public async Task Estado_ContaNova_NaoTem2FA()
	{
		var userId = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();

		var state = await setup.GetStateAsync(userId);

		state!.Enabled.Should().BeFalse();
		state.HasAuthenticator.Should().BeFalse();
	}

	[Fact]
	public async Task Cadastro_GeraChaveEQrNoProprioServidor()
	{
		var userId = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();

		var enrollment = await setup.StartEnrollmentAsync(userId);

		// O QR é embutido: um gerador externo receberia o segredo TOTP do usuário (ADR-0020).
		enrollment!.QrCodeDataUri.Should().StartWith("data:image/png;base64,");
		enrollment.FormattedKey.Should().MatchRegex("^[a-z0-9 ]+$");
	}

	[Fact]
	public async Task Cadastro_SoLigaDepoisDeConfirmarComCodigoValido()
	{
		var userId = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
		await setup.StartEnrollmentAsync(userId);

		// Sem a confirmação, um autenticador mal configurado trancaria a conta no login seguinte.
		(await setup.ConfirmAsync(userId, "000000")).Should().BeFalse();
		(await setup.GetStateAsync(userId))!.Enabled.Should().BeFalse();

		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
		var user = await userManager.FindByIdAsync(userId.ToString());
		var key = await userManager.GetAuthenticatorKeyAsync(user!);
		var codigo = TotpCalculator.Compute(key!);

		(await setup.ConfirmAsync(userId, codigo)).Should().BeTrue();
		(await setup.GetStateAsync(userId))!.Enabled.Should().BeTrue();
	}

	[Fact]
	public async Task CodigosDeRecuperacao_SaoDezENaoSeRepetem()
	{
		var userId = await UserAsync();

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();

		var codigos = await setup.GenerateRecoveryCodesAsync(userId);

		codigos.Should().HaveCount(10).And.OnlyHaveUniqueItems();
		(await setup.GetStateAsync(userId))!.RecoveryCodesLeft.Should().Be(10);
	}

	[Fact]
	public async Task Ligar_ComCodigoValido_DevolveCodigosEEncerraAsOutrasSessoes()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		var driver = new OidcLoginDriver(factory, ClientId, RedirectUri, IdentitySeed.Password);
		var (_, refreshToken) = await driver.LoginAsync(email, "openid offline_access logstream");

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
		var handler = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		await setup.StartEnrollmentAsync(userId);
		var user = await userManager.FindByIdAsync(userId.ToString());
		var codigos = await handler.HandleAsync(
			userId, TotpCalculator.Compute((await userManager.GetAuthenticatorKeyAsync(user!))!));

		codigos.Should().HaveCount(10);
		(await driver.RefreshAsync(refreshToken)).StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);

		var aviso = factory.Emails.For(email).Should().ContainSingle().Subject;
		aviso.Subject.Should().Contain("ativado");
		// O e-mail avisa; os códigos ficam só na tela, porque e-mail não é lugar de segredo.
		aviso.Body.Should().NotContainAny(codigos);
	}

	[Fact]
	public async Task Ligar_ComCodigoErrado_NaoLigaENaoAvisa()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
		var handler = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
		await setup.StartEnrollmentAsync(userId);

		(await handler.HandleAsync(userId, "000000")).Should().BeEmpty();
		(await setup.GetStateAsync(userId))!.Enabled.Should().BeFalse();
		factory.Emails.For(email).Should().BeEmpty();
	}

	[Fact]
	public async Task Desligar_ZeraOCadastroEAvisa()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);

		using var scope = factory.Services.CreateScope();
		var setup = scope.ServiceProvider.GetRequiredService<ITwoFactorSetup>();
		var enable = scope.ServiceProvider.GetRequiredService<EnableTwoFactorHandler>();
		var disable = scope.ServiceProvider.GetRequiredService<DisableTwoFactorHandler>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		await setup.StartEnrollmentAsync(userId);
		var user = await userManager.FindByIdAsync(userId.ToString());
		await enable.HandleAsync(userId, TotpCalculator.Compute((await userManager.GetAuthenticatorKeyAsync(user!))!));

		(await disable.HandleAsync(userId)).IsSuccess.Should().BeTrue();

		var state = await setup.GetStateAsync(userId);
		state!.Enabled.Should().BeFalse();
		// Desligar ZERA: religar exige cadastrar de novo, e um QR antigo guardado não serve.
		state.HasAuthenticator.Should().BeFalse();
		factory.Emails.For(email).Should().HaveCount(2);
		factory.Emails.For(email).Last().Subject.Should().Contain("desativado");
	}
}
