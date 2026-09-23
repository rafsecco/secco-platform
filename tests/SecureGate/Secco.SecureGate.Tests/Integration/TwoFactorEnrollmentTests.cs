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
	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
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

}
