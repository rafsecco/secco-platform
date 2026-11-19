using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Secco.SecureGate.Api.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>O desafio direto ao Entra fixa o diretório do tenant e repassa as dicas (ADR-0026).</summary>
public class EntraDirectChallengeTests
{
	private static readonly Guid Directory = Guid.Parse("11111111-2222-3333-4444-555555555555");

	private static RedirectContext NewContext(AuthenticationProperties properties, string issuer) =>
		new(new DefaultHttpContext(),
			new AuthenticationScheme("EntraId", null, typeof(OpenIdConnectHandler)),
			new OpenIdConnectOptions(),
			properties)
		{
			ProtocolMessage = new OpenIdConnectMessage { IssuerAddress = issuer },
		};

	[Fact]
	public void PinDirectory_Organizations_TrocaPeloDiretorio() =>
		EntraDirectChallenge.PinDirectory("https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize", Directory)
			.Should().Be($"https://login.microsoftonline.com/{Directory:D}/oauth2/v2.0/authorize");

	[Fact]
	public void PinDirectory_Common_TrocaPeloDiretorio() =>
		EntraDirectChallenge.PinDirectory("https://login.microsoftonline.com/COMMON/oauth2/v2.0/authorize", Directory)
			.Should().Be($"https://login.microsoftonline.com/{Directory:D}/oauth2/v2.0/authorize");

	[Fact]
	public void PinDirectory_NuvemSoberana_PreservaOHost() =>
		new Uri(EntraDirectChallenge.PinDirectory("https://login.microsoftonline.us/organizations/oauth2/v2.0/authorize", Directory)!)
			.Host.Should().Be("login.microsoftonline.us");

	[Theory]
	[InlineData("https://login.microsoftonline.com/aaaaaaaa-0000-0000-0000-000000000000/oauth2/v2.0/authorize")]
	[InlineData("https://login.microsoftonline.com/contoso.onmicrosoft.com/oauth2/v2.0/authorize")]
	public void PinDirectory_JaEspecifico_Nulo(string endpoint) =>
		EntraDirectChallenge.PinDirectory(endpoint, Directory).Should().BeNull();

	[Fact]
	public void PinDirectory_Http_Nulo() =>
		EntraDirectChallenge.PinDirectory("http://login.microsoftonline.com/organizations/oauth2/v2.0/authorize", Directory).Should().BeNull();

	[Fact]
	public void PinDirectory_Relativo_Nulo() =>
		EntraDirectChallenge.PinDirectory("/organizations/oauth2/v2.0/authorize", Directory).Should().BeNull();

	[Fact]
	public void Create_SemLoginHintNemDominio_SoDiretorio()
	{
		var props = EntraDirectChallenge.Create("/connect/authorize", new DirectSignInTarget(Guid.NewGuid(), Directory, null), null, false);

		props.Items.Keys.Should().NotContain(["secco.direct.domain_hint", "secco.direct.login_hint", "secco.direct.force_login"]);
		props.Items["secco.direct.directory"].Should().Be(Directory.ToString("D"));
	}

	[Fact]
	public void Create_RedirectUriApontaParaOCallbackComReturnUrlEscapado()
	{
		var props = EntraDirectChallenge.Create("/connect/authorize?a=1&b=2", new DirectSignInTarget(Guid.NewGuid(), Directory, "abbc.com.br"), "ana@abbc.com.br", true);

		props.RedirectUri.Should().Be("/login/entra-callback?returnUrl=" + Uri.EscapeDataString("/connect/authorize?a=1&b=2"));
		props.Items["secco.direct.domain_hint"].Should().Be("abbc.com.br");
		props.Items["secco.direct.login_hint"].Should().Be("ana@abbc.com.br");
		props.Items["secco.direct.force_login"].Should().Be("1");
	}

	[Fact]
	public async Task OnRedirect_ComItens_AplicaEndpointDominioLoginHintEPrompt()
	{
		var props = EntraDirectChallenge.Create("/x", new DirectSignInTarget(Guid.NewGuid(), Directory, "abbc.com.br"), "ana@abbc.com.br", true);
		var context = NewContext(props, "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize");

		await EntraDirectChallenge.OnRedirectToIdentityProvider(context);

		context.ProtocolMessage.IssuerAddress.Should().Be($"https://login.microsoftonline.com/{Directory:D}/oauth2/v2.0/authorize");
		context.ProtocolMessage.DomainHint.Should().Be("abbc.com.br");
		context.ProtocolMessage.LoginHint.Should().Be("ana@abbc.com.br");
		context.ProtocolMessage.Prompt.Should().Be("login");
	}

	[Fact]
	public async Task OnRedirect_SemItemDeDiretorio_NaoMexeEmNada()
	{
		var issuer = "https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize";
		var context = NewContext(new AuthenticationProperties(), issuer);

		await EntraDirectChallenge.OnRedirectToIdentityProvider(context);

		context.ProtocolMessage.IssuerAddress.Should().Be(issuer);
		context.ProtocolMessage.DomainHint.Should().BeNull();
		context.ProtocolMessage.LoginHint.Should().BeNull();
		context.ProtocolMessage.Prompt.Should().BeNull();
	}
}
