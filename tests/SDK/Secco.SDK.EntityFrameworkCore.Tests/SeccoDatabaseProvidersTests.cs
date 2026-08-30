using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests;

public class SeccoDatabaseProvidersTests
{
	private sealed class FakeDbContext : DbContext;

	[Fact]
	public void Configure_WithMatchingName_AppliesOnlyTheMatchingRegistration()
	{
		var applied = new List<string>();
		var registrations = new[]
		{
			new SeccoDatabaseProviderRegistration("SqlServer", (_, cs) => applied.Add($"SqlServer:{cs}")),
			new SeccoDatabaseProviderRegistration("PostgreSql", (_, cs) => applied.Add($"PostgreSql:{cs}")),
		};
		var builder = new DbContextOptionsBuilder<FakeDbContext>();

		SeccoDatabaseProviders.Configure(builder, "PostgreSql", "Server=x", registrations);

		applied.Should().Equal("PostgreSql:Server=x");
	}

	[Theory]
	[InlineData("sqlserver")]
	[InlineData("SQLSERVER")]
	[InlineData("SqlServer")]
	public void Configure_NameComparison_IsOrdinalIgnoreCase(string providerName)
	{
		var applied = new List<string>();
		var registrations = new[]
		{
			new SeccoDatabaseProviderRegistration("SqlServer", (_, _) => applied.Add("SqlServer")),
		};
		var builder = new DbContextOptionsBuilder<FakeDbContext>();

		SeccoDatabaseProviders.Configure(builder, providerName, "cs", registrations);

		applied.Should().Equal("SqlServer");
	}

	[Fact]
	public void Configure_WithUnknownName_ThrowsCitingReceivedValueAndAcceptedList()
	{
		var registrations = new[]
		{
			new SeccoDatabaseProviderRegistration("SqlServer", (_, _) => { }),
			new SeccoDatabaseProviderRegistration("PostgreSql", (_, _) => { }),
		};
		var builder = new DbContextOptionsBuilder<FakeDbContext>();

		var act = () => SeccoDatabaseProviders.Configure(
			builder, "Oracle", "Server=secret;Password=abc123", registrations);

		var exception = act.Should().Throw<ArgumentException>().Which;
		exception.ParamName.Should().Be("providerName");
		exception.Message.Should().Contain("Oracle");
		exception.Message.Should().Contain("SqlServer");
		exception.Message.Should().Contain("PostgreSql");
		exception.Message.Should().NotContain("Server=secret;Password=abc123",
			"a mensagem de fail-fast nunca pode citar a connection string (ADR-0020)");
	}

	[Fact]
	public void CreateOptions_WithMatchingName_AppliesTheRegistrationAndReturnsOptions()
	{
		var applied = new List<string>();
		var registrations = new[]
		{
			new SeccoDatabaseProviderRegistration("SqlServer", (_, cs) => applied.Add($"SqlServer:{cs}")),
		};

		var options = SeccoDatabaseProviders.CreateOptions<FakeDbContext>("SqlServer", "Server=y", registrations);

		options.Should().NotBeNull();
		applied.Should().Equal("SqlServer:Server=y");
	}

	[Fact]
	public void CreateOptions_WithUnknownName_ThrowsCitingReceivedValueAndAcceptedList()
	{
		var registrations = new[]
		{
			new SeccoDatabaseProviderRegistration("SqlServer", (_, _) => { }),
		};

		var act = () => SeccoDatabaseProviders.CreateOptions<FakeDbContext>("Oracle", "cs", registrations);

		act.Should().Throw<ArgumentException>().WithMessage("*Oracle*").WithMessage("*SqlServer*");
	}
}
