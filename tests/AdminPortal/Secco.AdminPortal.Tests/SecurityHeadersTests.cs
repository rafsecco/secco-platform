using FluentAssertions;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// Cabeçalhos de segurança do console (ADR-0057): nunca emoldurado, e o logout pode terminar no
/// SecureGate porque a origem dele está no <c>form-action</c>.
/// </summary>
public class SecurityHeadersTests(HealthCheckTests.AdminPortalFactory factory) : IClassFixture<HealthCheckTests.AdminPortalFactory>
{
	private static string Header(HttpResponseMessage response, string name)
		=> response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : string.Empty;

	[Fact]
	public async Task Console_nunca_e_emoldurado_e_o_logout_alcanca_o_securegate()
	{
		var response = await factory.CreateClient().GetAsync("/health/live");

		Header(response, "X-Frame-Options").Should().Be("DENY");
		Header(response, "X-Content-Type-Options").Should().Be("nosniff");
		Header(response, "Content-Security-Policy")
			.Should().Contain("frame-ancestors 'none'")
			.And.Contain("form-action 'self' https://localhost")
			.And.Contain("style-src-attr 'none'");
	}

	[Fact]
	public async Task Arquivo_estatico_tambem_leva_os_cabecalhos()
	{
		var response = await factory.CreateClient().GetAsync("/app.css");

		response.EnsureSuccessStatusCode();
		Header(response, "X-Content-Type-Options").Should().Be("nosniff");
		Header(response, "Content-Security-Policy").Should().NotBeEmpty();
	}
}
