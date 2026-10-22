using FluentAssertions;
using Secco.SecureGate.Infrastructure.Seeding;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Produtos adotantes do catálogo de permissões (issue #49): só nome que a rota de publicação aceitaria vira escopo.
/// </summary>
public class PermissionCatalogOptionsValidatorTests
{
	private static bool Valid(params string[] products) =>
		new PermissionCatalogOptionsValidator()
			.Validate(null, new PermissionCatalogOptions { Products = [.. products] }).Succeeded;

	[Fact]
	public void SemProdutos_Sucesso() => Valid().Should().BeTrue();

	[Fact]
	public void KebabCase_EProdutoDaPlataforma_Sucesso() => Valid("intranet", "compras-web", "logstream").Should().BeTrue();

	[Theory]
	[InlineData("Intranet")]
	[InlineData("intranet:read")]
	[InlineData("com espaco")]
	[InlineData("")]
	[InlineData("produto-com-um-nome-muito-maior-que-o-limite-de-cinquenta")]
	public void NomeQueARotaNaoAceitaria_Falha(string product) => Valid("intranet", product).Should().BeFalse();
}
