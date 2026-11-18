using FluentAssertions;
using Secco.SecureGate.Application.Federation;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Regras dos atributos de pessoa lidos do diretório federado: input externo, então texto fora do limite
/// ou com caractere de controle é descartado (nunca truncado) — ADR-0020.
/// </summary>
public class DirectoryAttributeRulesTests
{
	[Fact]
	public void Normalize_Nulo_ViraNuloSemDescarte()
	{
		DirectoryAttributeRules.Normalize(null, 128, out var discarded).Should().BeNull();
		discarded.Should().BeFalse();
	}

	[Fact]
	public void Normalize_SoEspacos_ViraNuloSemDescarte()
	{
		DirectoryAttributeRules.Normalize("   ", 128, out var discarded).Should().BeNull();
		discarded.Should().BeFalse();
	}

	[Fact]
	public void Normalize_ComEspacosNasPontas_Apara()
	{
		DirectoryAttributeRules.Normalize("  Analista  ", 128, out var discarded).Should().Be("Analista");
		discarded.Should().BeFalse();
	}

	[Fact]
	public void Normalize_NoLimite_Aceita()
	{
		var text = new string('a', 128);
		DirectoryAttributeRules.Normalize(text, 128, out var discarded).Should().Be(text);
		discarded.Should().BeFalse();
	}

	[Fact]
	public void Normalize_AcimaDoLimite_DescartaSemTruncar()
	{
		DirectoryAttributeRules.Normalize(new string('a', 129), 128, out var discarded).Should().BeNull();
		discarded.Should().BeTrue();
	}

	[Theory]
	[InlineData("Ana\nlista")]
	[InlineData("Ana\rlista")]
	[InlineData("Ana\u0000")]
	[InlineData("Ana\u001b[31m")]
	[InlineData("Ana\u0085")]
	public void Normalize_ComCaractereDeControle_Descarta(string raw)
	{
		DirectoryAttributeRules.Normalize(raw, 128, out var discarded).Should().BeNull();
		discarded.Should().BeTrue();
	}

	[Fact]
	public void Normalize_LimiteValeDepoisDoTrim()
	{
		var core = new string('a', 64);
		DirectoryAttributeRules.Normalize(" " + core + " ", 64, out var discarded).Should().Be(core);
		discarded.Should().BeFalse();
	}
}
