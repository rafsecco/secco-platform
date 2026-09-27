using FluentAssertions;
using Secco.SecureGate.Application.Federation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Sanitização do termo de busca antes de entrar no <c>$filter</c> OData (ADR-0020/0036) — o
/// equivalente a injeção de SQL para o Graph.
/// </summary>
public class EntraGroupNameFilterTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Sanitize_VazioOuSoEspaco_RetornaNull(string? input) =>
		EntraGroupNameFilter.Sanitize(input).Should().BeNull();

	[Fact]
	public void Sanitize_NomeNormal_AparaEDevolveIgual() =>
		EntraGroupNameFilter.Sanitize("  Financeiro  ").Should().Be("Financeiro");

	[Fact]
	public void Sanitize_AspaSimples_DobraAAspa() =>
		// O'Brien é um nome de grupo válido; sem dobrar, a aspa fecharia a string do $filter OData
		// e o resto do termo viraria sintaxe controlada por quem digitou a busca.
		EntraGroupNameFilter.Sanitize("O'Brien").Should().Be("O''Brien");

	[Theory]
	[InlineData("Financeiro\r\nOutraLinha")]
	[InlineData("Financeiro\tTab")]
	[InlineData("Financeiro\0Nulo")]
	public void Sanitize_ComCaractereDeControle_Lanca(string input) =>
		FluentActions.Invoking(() => EntraGroupNameFilter.Sanitize(input))
			.Should().Throw<ArgumentException>();

	[Fact]
	public void Sanitize_AcimaDoLimite_Lanca() =>
		FluentActions.Invoking(() => EntraGroupNameFilter.Sanitize(new string('a', 101)))
			.Should().Throw<ArgumentException>();

	[Fact]
	public void Sanitize_NoLimite_Aceita() =>
		EntraGroupNameFilter.Sanitize(new string('a', EntraGroupNameFilter.MaxLength)).Should().HaveLength(100);
}
