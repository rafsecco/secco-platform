using FluentAssertions;
using Secco.SharedKernel.Authorization;
using Xunit;

namespace Secco.SharedKernel.Tests.Authorization;

public class SeccoPermissionDefinitionTests
{
	[Fact]
	public void Equality_WithSameValues_IsEqual() =>
		new SeccoPermissionDefinition("log-entries:read", "Ler logs")
			.Should().Be(new SeccoPermissionDefinition("log-entries:read", "Ler logs"));

	[Fact]
	public void Equality_WithDifferentDescription_IsNotEqual() =>
		new SeccoPermissionDefinition("log-entries:read", "Ler logs")
			.Should().NotBe(new SeccoPermissionDefinition("log-entries:read", "Outra"));
}
