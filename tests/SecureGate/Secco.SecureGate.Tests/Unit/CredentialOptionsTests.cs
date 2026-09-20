using FluentAssertions;
using Secco.SecureGate.Infrastructure.Credentials;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Fail-fast da configuração de credenciais (ADR-0033/ADR-0020). A base pública dos links é a
/// peça mais sensível: derivá-la do header <c>Host</c> faria a plataforma enviar à vítima um
/// link apontando para o servidor do atacante, então ela vem de configuração ou o serviço não sobe.
/// </summary>
public class CredentialOptionsTests
{
	[Fact]
	public void Options_ForaDeDevelopmentSemBaseUrl_Invalida()
	{
		var options = new CredentialOptions();

		options.TryValidate(isDevelopment: false, out var error).Should().BeFalse();
		error.Should().Contain(CredentialOptions.PublicBaseUrlKey);
	}

	[Fact]
	public void Options_BaseUrlRelativa_Invalida()
	{
		var options = new CredentialOptions { PublicBaseUrl = "/conta" };

		options.TryValidate(isDevelopment: false, out var error).Should().BeFalse();
		error.Should().Contain(CredentialOptions.PublicBaseUrlKey);
	}

	[Fact]
	public void Options_BaseUrlComEsquemaEstranho_Invalida()
	{
		var options = new CredentialOptions { PublicBaseUrl = "ftp://id.exemplo" };

		options.TryValidate(isDevelopment: false, out _).Should().BeFalse();
	}

	[Theory]
	[InlineData(0)]
	[InlineData(721)]
	public void Options_ValidadeDeConviteForaDoTeto_Invalida(int hours)
	{
		var options = new CredentialOptions { PublicBaseUrl = "https://id.exemplo", InviteLifetimeHours = hours };

		options.TryValidate(isDevelopment: false, out var error).Should().BeFalse();
		error.Should().Contain("InviteLifetimeHours");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(121)]
	public void Options_ValidadeDeRedefinicaoForaDoTeto_Invalida(int minutes)
	{
		var options = new CredentialOptions { PublicBaseUrl = "https://id.exemplo", ResetLifetimeMinutes = minutes };

		options.TryValidate(isDevelopment: false, out var error).Should().BeFalse();
		error.Should().Contain("ResetLifetimeMinutes");
	}

	[Fact]
	public void Options_LimiteDeRecuperacaoZerado_Invalida()
	{
		var options = new CredentialOptions { PublicBaseUrl = "https://id.exemplo", ForgotPerAccountPerHour = 0 };

		options.TryValidate(isDevelopment: false, out var error).Should().BeFalse();
		error.Should().Contain("ForgotPerAccountPerHour");
	}

	[Fact]
	public void Options_EmDevelopmentSemBaseUrl_ValidaComPadraoLocal()
	{
		var options = new CredentialOptions();

		options.TryValidate(isDevelopment: true, out _).Should().BeTrue();
		options.ResolveBaseUri().ToString().Should().StartWith("https://localhost:4001");
	}

	[Fact]
	public void Options_BaseUrlComBarraFinal_NaoDuplicaAoMontarOLink()
	{
		var options = new CredentialOptions { PublicBaseUrl = "https://id.exemplo/" };

		options.BuildLink("/conta/definir-senha", "userId=1&token=abc")
			.Should().Be("https://id.exemplo/conta/definir-senha?userId=1&token=abc");
	}

	[Fact]
	public void Options_Padroes_SaoOsDaAdr0033()
	{
		var options = new CredentialOptions();

		options.InviteLifetimeHours.Should().Be(72);
		options.ResetLifetimeMinutes.Should().Be(30);
		options.ForgotPerAccountPerHour.Should().Be(3);
		options.ForgotPerIpPerHour.Should().Be(10);
	}
}
