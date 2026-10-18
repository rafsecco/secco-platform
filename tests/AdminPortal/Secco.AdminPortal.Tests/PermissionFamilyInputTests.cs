using FluentAssertions;
using Secco.AdminPortal.Services;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>Composição e agrupamento de instâncias de família na edição de perfil (ADR-0043).</summary>
public class PermissionFamilyInputTests
{
	private const string Family = "setor-{id}:read";

	[Theory]
	[InlineData("rh", "setor-rh:read")]
	[InlineData("  recursos-humanos  ", "setor-recursos-humanos:read")]
	[InlineData("42", "setor-42:read")]
	public void TryCompose_WithValidValue_ReturnsPermission(string value, string expected)
	{
		var ok = PermissionFamilyInput.TryCompose(Family, value, out var permission, out var error);

		ok.Should().BeTrue();
		permission.Should().Be(expected);
		error.Should().BeEmpty();
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("RH")]
	[InlineData("rh:write")]
	[InlineData("-rh")]
	[InlineData("rh-")]
	[InlineData("rh com espaco")]
	public void TryCompose_WithInvalidValue_ReturnsErrorWithoutThrowing(string? value)
	{
		var ok = PermissionFamilyInput.TryCompose(Family, value, out var permission, out var error);

		ok.Should().BeFalse();
		permission.Should().BeEmpty();
		error.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void InstancesOf_ReturnsOnlyMatchesOfTheFamily_Ordered()
	{
		var instances = PermissionFamilyInput.InstancesOf(Family,
			["setor-rh:read", "setor-rh:write", "logs:read", "setor-:read", "setor-compras:read", "setores-x:read"]);

		instances.Should().Equal("setor-compras:read", "setor-rh:read");
	}
}
