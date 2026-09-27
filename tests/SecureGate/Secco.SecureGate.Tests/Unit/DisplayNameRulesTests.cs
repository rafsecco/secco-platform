using FluentAssertions;
using Secco.SecureGate.Application.Users;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>
/// Regras de formato do nome de exibição (#30). Diferente do nome de role, aceita acento, espaço,
/// hífen e apóstrofo — só exclui caractere de controle (log forging na trilha e no token, ADR-0020).
/// </summary>
public class DisplayNameRulesTests
{
	[Theory]
	[InlineData("  Rafael Secco  ", "Rafael Secco")]
	[InlineData("O'Connor-Silva Jr.", "O'Connor-Silva Jr.")]
	[InlineData("Ana Cláudia", "Ana Cláudia")]
	public void Normalize_ApareEspacos(string input, string expected) =>
		DisplayNameRules.Normalize(input).Should().Be(expected);

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void Normalize_VazioOuSoEspaco_ViraNull(string? input) =>
		DisplayNameRules.Normalize(input).Should().BeNull();

	[Fact]
	public void IsValid_NomeNormal_Aceita() =>
		DisplayNameRules.IsValid("Rafael Secco").Should().BeTrue();

	[Fact]
	public void IsValid_NoLimite_Aceita() =>
		DisplayNameRules.IsValid(new string('a', DisplayNameRules.MaxLength)).Should().BeTrue();

	[Fact]
	public void IsValid_AcimaDoLimite_Recusa() =>
		// 161, fixo: o limite documentado é 160. Um valor relativo a MaxLength não pegaria uma
		// mutação que muda a própria constante — o teste recalcularia o limite junto com ela.
		DisplayNameRules.IsValid(new string('a', 161)).Should().BeFalse();

	[Theory]
	[InlineData("Rafael\r\nSecco")]
	[InlineData("Rafael\tSecco")]
	[InlineData("Rafael\0Secco")]
	public void IsValid_ComCaractereDeControle_Recusa(string name) =>
		// O valor viaja para a claim do token e para a trilha de auditoria — CR/LF forjaria uma
		// segunda linha de log ou um segundo header; não é um risco de HTML (Razor já codifica).
		DisplayNameRules.IsValid(name).Should().BeFalse();

	[Fact]
	public void IsValid_Vazio_Recusa() =>
		// Normalize já converteria em null antes; IsValid não recebe vazio no caminho normal, mas
		// não deve aceitar se receber — nenhuma chamada direta pode contornar a regra.
		DisplayNameRules.IsValid(string.Empty).Should().BeFalse();
}
