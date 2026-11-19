using FluentAssertions;
using Secco.SecureGate.Api.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>A recusa do login federado preserva só <c>returnUrl</c> local (ADR-0020).</summary>
public class FederatedFailureRedirectTests
{
	[Fact]
	public void FederatedFailureRedirect_For_Local_PreservaReturnUrl() =>
		FederatedFailureRedirect.For("/connect/authorize?a=1&b=2")
			.Should().Be("/login?federatedError=1&returnUrl=" + Uri.EscapeDataString("/connect/authorize?a=1&b=2"));

	[Fact]
	public void FederatedFailureRedirect_For_Absoluta_Descarta() =>
		FederatedFailureRedirect.For("https://evil.example/x").Should().Be("/login?federatedError=1");

	[Fact]
	public void FederatedFailureRedirect_For_BarraDupla_Descarta() =>
		FederatedFailureRedirect.For("//evil.example/x").Should().Be("/login?federatedError=1");

	[Fact]
	public void FederatedFailureRedirect_For_Nulo_SoOPath() =>
		FederatedFailureRedirect.For(null).Should().Be("/login?federatedError=1");

	[Fact]
	public void FederatedFailureRedirect_FromCallbackRedirectUri_ExtraiOReturnUrl() =>
		FederatedFailureRedirect.FromCallbackRedirectUri("/login/entra-callback?returnUrl=" + Uri.EscapeDataString("/connect/authorize?a=1"))
			.Should().Be("/login?federatedError=1&returnUrl=" + Uri.EscapeDataString("/connect/authorize?a=1"));

	[Fact]
	public void FederatedFailureRedirect_FromCallbackRedirectUri_SemReturnUrl_SoOPath() =>
		FederatedFailureRedirect.FromCallbackRedirectUri("/login/entra-callback").Should().Be("/login?federatedError=1");
}
