using FluentAssertions;
using Secco.SecureGate.Api.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

/// <summary>O <c>login_hint</c> é input externo (ADR-0020): só e-mail plausível passa ao Entra.</summary>
public class LoginHintTests
{
	[Theory]
	[InlineData("ana@abbc.com.br", "ana@abbc.com.br")]
	[InlineData(null, null)]
	[InlineData("", null)]
	[InlineData("ana@abbc.com\r\nX: y", null)]
	[InlineData("ana abbc@x", null)]
	[InlineData("sem-arroba", null)]
	[InlineData("a@b@c", null)]
	[InlineData("@abbc.com", null)]
	[InlineData("ana@", null)]
	public void Sanitize_AceitaSoEmailPlausivel(string? value, string? expected)
	{
		LoginHint.Sanitize(value).Should().Be(expected);
	}

	[Fact]
	public void Sanitize_Acima_Do_Limite_Nulo()
	{
		var value = new string('a', LoginHint.MaxLength - 1) + "@b";

		value.Length.Should().Be(LoginHint.MaxLength + 1);
		LoginHint.Sanitize(value).Should().BeNull();
	}

	[Fact]
	public void Sanitize_ExatamenteNoLimite_Passa()
	{
		var value = new string('a', LoginHint.MaxLength - 2) + "@b";

		value.Length.Should().Be(LoginHint.MaxLength);
		LoginHint.Sanitize(value).Should().Be(value);
	}
}
