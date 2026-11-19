using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>O evento de redirecionamento está ligado ao esquema do Entra e não altera o botão da tela de login.</summary>
[Collection(EntraFederatedApiCollectionDefinition.Name)]
public class EntraSchemeWiringTests(EntraFederatedSecureGateApiFactory factory) : IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task LoginPostEntra_SemItemDeDiretorio_DesafiaOrganizationsSemHints()
	{
		using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

		var page = await client.GetAsync("/login");
		page.StatusCode.Should().Be(HttpStatusCode.OK);
		var token = OidcLoginDriver.ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

		var response = await client.PostAsync("/login?handler=Entra", new FormUrlEncodedContent(
		[
			new KeyValuePair<string, string>("__RequestVerificationToken", token),
		]));

		response.StatusCode.Should().Be(HttpStatusCode.Redirect);
		var location = response.Headers.Location!.ToString();
		location.Should().StartWith("https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize");
		location.Should().NotContain("domain_hint").And.NotContain("login_hint");
	}
}
