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
	[InlineData("securegate")]
	[InlineData("openid")]
	[InlineData("LOGSTREAM")]
	[InlineData("")]
	public void IsProductScope_EscopoDeInfraestruturaOuDesconhecido_Recusa(string scope) =>
		SecureGateScopes.IsProductScope(scope).Should().BeFalse();
}
