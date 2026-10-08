using FluentAssertions;
using Secco.SecureGate.Application.Clients;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

public class ProductClientRulesTests
{
	[Fact]
	public void NewClientId_SempreTemPrefixoE16CaracteresBase32Minusculos()
	{
		var clientId = ProductClientRules.NewClientId();

		clientId.Should().MatchRegex("^cli_[a-z2-7]{16}$");
	}

	[Fact]
	public void NewClientId_DuasChamadas_GeramValoresDiferentes() =>
		ProductClientRules.NewClientId().Should().NotBe(ProductClientRules.NewClientId());

	[Fact]
	public void NewSecret_Sempre32BytesEmBase64Url()
	{
		var secret = ProductClientRules.NewSecret();

		secret.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "32 bytes em base64url sem padding são 43 caracteres");
	}

	[Theory]
	[InlineData("Sistema de compras", true)]
	[InlineData("a", true)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData("nome\u0007com controle", false)]
	public void IsValidName_CasosDeFormato(string name, bool expected) =>
		ProductClientRules.IsValidName(name.Trim()).Should().Be(expected);

	[Fact]
	public void IsValidName_AcimaDe100_Recusa() =>
		ProductClientRules.IsValidName(new string('x', 101)).Should().BeFalse();
}
