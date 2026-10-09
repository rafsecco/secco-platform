using FluentAssertions;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Migrations;

public class SeccoCommandsTests
{
	[Fact]
	public void IsMigrate_PrimeiroArgumentoMigrate_True() => SeccoCommands.IsMigrate(["migrate"]).Should().BeTrue();

	[Theory]
	[InlineData()]
	[InlineData("Migrate")]
	[InlineData("--migrate")]
	[InlineData("x", "migrate")]
	public void IsMigrate_QualquerOutraForma_False(params string[] args) => SeccoCommands.IsMigrate(args).Should().BeFalse();
}
