using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

public class ProductScopesTests
{
	[Theory]
	[InlineData("logstream")]
	[InlineData("notificationhub")]
	public void IsProductScope_EscopoDeApiDeProduto_Aceita(string scope) =>
		SecureGateScopes.IsProductScope(scope).Should().BeTrue();

	[Theory]
	[InlineData("securegate:admin")]
	[InlineData("authorization:read")]
	[InlineData("catalog:logstream")]
	[InlineData("permissions:logstream")]
	[InlineData("securegate")]
	[InlineData("openid")]
	[InlineData("LOGSTREAM")]
	[InlineData("")]
	public void IsProductScope_EscopoDeInfraestruturaOuDesconhecido_Recusa(string scope) =>
		SecureGateScopes.IsProductScope(scope).Should().BeFalse();

	[Fact]
	public void IsProductScope_DiretorioSoParaClientDeMaquina()
	{
		SecureGateScopes.IsProductScope(SecureGateScopes.DirectoryRead, interactive: false).Should().BeTrue();
		SecureGateScopes.IsProductScope(SecureGateScopes.DirectoryRead, interactive: true).Should().BeFalse();
		SecureGateScopes.IsProductScope(SecureGateScopes.DirectoryRead).Should().BeTrue("o padrão é client_credentials");
	}
}
