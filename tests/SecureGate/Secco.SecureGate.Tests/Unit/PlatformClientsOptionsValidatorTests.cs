using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Secco.SecureGate.Infrastructure.Clients;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

public class PlatformClientsOptionsValidatorTests
{
	private const string GoodSecret = "segredo-de-plataforma-com-32-chars!!";

	private static PlatformClientsOptionsValidator Validator(string environment)
	{
		var host = Substitute.For<IHostEnvironment>();
		host.EnvironmentName.Returns(environment);
		return new PlatformClientsOptionsValidator(host);
	}

	private static PlatformClientsOptions With(params PlatformClientDefinition[] clients) =>
		new() { PlatformClients = [.. clients] };

	private static PlatformClientDefinition Machine(string clientId = "secco-logstream", string? secret = GoodSecret) =>
		new() { ClientId = clientId, Type = PlatformClientType.ClientCredentials, ClientSecret = secret, Scopes = ["catalog:logstream"] };

	[Fact]
	public void Validate_SemClients_Sucesso() =>
		Validator(Environments.Production).Validate(null, With()).Succeeded.Should().BeTrue();

	[Fact]
	public void Validate_ClientValido_Sucesso() =>
		Validator(Environments.Production).Validate(null, With(Machine())).Succeeded.Should().BeTrue();

	[Theory]
	[InlineData("cli_abc")]
	[InlineData("Maiuscula")]
	[InlineData("com espaco")]
	[InlineData("")]
	public void Validate_ClientIdInvalido_Falha(string clientId) =>
		Validator(Environments.Production).Validate(null, With(Machine(clientId))).Failed.Should().BeTrue();

	[Fact]
	public void Validate_ClientIdRepetido_Falha() =>
		Validator(Environments.Production).Validate(null, With(Machine(), Machine())).Failed.Should().BeTrue();

	[Fact]
	public void Validate_SecretCurtoForaDeDev_Falha() =>
		Validator(Environments.Production).Validate(null, With(Machine(secret: "curto"))).Failed.Should().BeTrue();

	[Fact]
	public void Validate_SecretCurtoEmDev_Sucesso() =>
		Validator(Environments.Development).Validate(null, With(Machine(secret: "curto"))).Succeeded.Should().BeTrue();

	[Fact]
	public void Validate_SecretAusente_FalhaEmQualquerAmbiente() =>
		Validator(Environments.Development).Validate(null, With(Machine(secret: null))).Failed.Should().BeTrue();

	[Fact]
	public void Validate_TipoAusente_Falha() =>
		Validator(Environments.Production).Validate(null, With(new PlatformClientDefinition
		{
			ClientId = "x", ClientSecret = GoodSecret, Scopes = ["logstream"],
		})).Failed.Should().BeTrue();

	[Fact]
	public void Validate_AuthorizationCodeSemRedirect_Falha() =>
		Validator(Environments.Production).Validate(null, With(new PlatformClientDefinition
		{
			ClientId = "secco-adminportal", Type = PlatformClientType.AuthorizationCode, ClientSecret = GoodSecret,
			Scopes = ["openid"],
		})).Failed.Should().BeTrue();

	[Fact]
	public void Validate_RedirectHttpForaDeDev_Falha() =>
		Validator(Environments.Production).Validate(null, With(new PlatformClientDefinition
		{
			ClientId = "secco-adminportal", Type = PlatformClientType.AuthorizationCode, ClientSecret = GoodSecret,
			Scopes = ["openid"], RedirectUris = ["http://portal.empresa.local/signin-oidc"],
		})).Failed.Should().BeTrue();

	[Fact]
	public void Validate_MensagemDeFalha_NuncaContemOSecret()
	{
		var result = Validator(Environments.Production).Validate(null, With(Machine(clientId: "cli_x", secret: GoodSecret)));

		result.FailureMessage.Should().NotContain(GoodSecret);
	}
}
