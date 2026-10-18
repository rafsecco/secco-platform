using FluentAssertions;
using Secco.SharedKernel.Authorization;
using Xunit;

namespace Secco.SharedKernel.Tests.Authorization;

public class SeccoPermissionTests
{
	[Theory]
	[InlineData("log-entries:read")]
	[InlineData("invoices:write")]
	[InlineData("api-call-logs:read")]
	[InlineData("a:b")]
	[InlineData("recurso1:acao-composta-2")]
	public void IsValid_WithCanonicalFormat_ReturnsTrue(string permission) =>
		SeccoPermissions.IsValid(permission).Should().BeTrue();

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("semseparador")]
	[InlineData("recurso:")]
	[InlineData(":acao")]
	[InlineData("Recurso:Acao")]
	[InlineData("recurso:acao:extra")]
	[InlineData("recurso :acao")]
	[InlineData("recurso:ação")]
	[InlineData("-recurso:acao")]
	[InlineData("recurso-:acao")]
	public void IsValid_WithInvalidFormat_ReturnsFalse(string? permission) =>
		SeccoPermissions.IsValid(permission).Should().BeFalse();

	[Fact]
	public void IsValid_AboveMaxLength_ReturnsFalse()
	{
		var permission = $"{new string('a', SeccoPermissions.MaxLength)}:read";

		SeccoPermissions.IsValid(permission).Should().BeFalse();
	}

	[Fact]
	public void Create_WithValidParts_ComposesPermission() =>
		SeccoPermissions.Create("log-entries", "read").Should().Be("log-entries:read");

	[Theory]
	[InlineData("Recurso", "read")]
	[InlineData("recurso", "")]
	[InlineData("recurso com espaco", "read")]
	public void Create_WithInvalidParts_Throws(string resource, string action)
	{
		var act = () => SeccoPermissions.Create(resource, action);

		act.Should().Throw<ArgumentException>();
	}

	[Theory]
	[InlineData("setor-{id}:read")]
	[InlineData("centro-de-custo-{codigo}:write")]
	[InlineData("a-{x}:b")]
	[InlineData("projeto1-{id}:acao-composta")]
	public void IsValidFamily_WithFamilyFormat_ReturnsTrue(string family) =>
		SeccoPermissions.IsValidFamily(family).Should().BeTrue();

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("setor:read")]
	[InlineData("{x}:read")]
	[InlineData("setor-{id}:{acao}")]
	[InlineData("setor-{id}:re-{x}")]
	[InlineData("setor-{id}-{x}:read")]
	[InlineData("a-{x}-b:read")]
	[InlineData("setor{id}:read")]
	[InlineData("setor-{ID}:read")]
	[InlineData("setor-{id1}:read")]
	[InlineData("setor-{}:read")]
	[InlineData("Setor-{id}:read")]
	[InlineData("setor-{id}:")]
	[InlineData("setor-{id}")]
	[InlineData("setor-{id}:read:extra")]
	[InlineData("setor-*:read")]
	public void IsValidFamily_WithInvalidFormat_ReturnsFalse(string? family) =>
		SeccoPermissions.IsValidFamily(family).Should().BeFalse();

	[Fact]
	public void IsValidFamily_AboveMaxLength_ReturnsFalse()
	{
		var family = $"{new string('a', SeccoPermissions.MaxLength)}-{{id}}:read";

		SeccoPermissions.IsValidFamily(family).Should().BeFalse();
	}

	[Fact]
	public void IsValid_WithFamily_ReturnsFalse() =>
		SeccoPermissions.IsValid("setor-{id}:read").Should().BeFalse();

	[Theory]
	[InlineData("setor-{id}:read", "setor-rh:read")]
	[InlineData("setor-{id}:read", "setor-recursos-humanos:read")]
	[InlineData("setor-{id}:read", "setor-1:read")]
	[InlineData("setor-{id}:acao-composta", "setor-rh:acao-composta")]
	public void MatchesFamily_WithInstance_ReturnsTrue(string family, string permission) =>
		SeccoPermissions.MatchesFamily(family, permission).Should().BeTrue();

	[Theory]
	[InlineData("setor-{id}:read", "setor-rh:write")]
	[InlineData("setor-{id}:read", "setor-:read")]
	[InlineData("setor-{id}:read", "setor:read")]
	[InlineData("setor-{id}:read", "setor--rh:read")]
	[InlineData("setor-{id}:read", "setores-x:read")]
	[InlineData("setor-{id}:read", "xsetor-rh:read")]
	[InlineData("setor-{id}:read", "setor-rh:read:extra")]
	[InlineData("setor-{id}:read", "setor-rh:Read")]
	[InlineData("setor-{id}:read", "setor-{id}:read")]
	[InlineData("setor-{id}:read", "")]
	[InlineData("setor:read", "setor-rh:read")]
	[InlineData("{x}:read", "setor-rh:read")]
	public void MatchesFamily_WithoutMatch_ReturnsFalse(string family, string permission) =>
		SeccoPermissions.MatchesFamily(family, permission).Should().BeFalse();

	[Fact]
	public void FromFamily_WithValidValue_ComposesPermissionThatMatchesFamily()
	{
		var permission = SeccoPermissions.FromFamily("setor-{id}:read", "recursos-humanos");

		permission.Should().Be("setor-recursos-humanos:read");
		SeccoPermissions.IsValid(permission).Should().BeTrue();
		SeccoPermissions.MatchesFamily("setor-{id}:read", permission).Should().BeTrue();
	}

	[Theory]
	[InlineData("setor-{id}:read", "")]
	[InlineData("setor-{id}:read", "RH")]
	[InlineData("setor-{id}:read", "rh:write")]
	[InlineData("setor-{id}:read", "rh com espaco")]
	[InlineData("setor-{id}:read", "-rh")]
	[InlineData("setor-{id}:read", "rh-")]
	public void FromFamily_WithInvalidValue_Throws(string family, string value)
	{
		var act = () => SeccoPermissions.FromFamily(family, value);

		act.Should().Throw<ArgumentException>();
	}

	[Theory]
	[InlineData("setor:read")]
	[InlineData("{x}:read")]
	public void FromFamily_WithInvalidFamily_Throws(string family)
	{
		var act = () => SeccoPermissions.FromFamily(family, "rh");

		act.Should().Throw<ArgumentException>();
	}
}
