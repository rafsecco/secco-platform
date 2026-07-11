using FluentAssertions;
using Secco.SDK.EntityFrameworkCore.Conventions;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Conventions;

public class SnakeCaseNamerTests
{
    [Theory]
    [InlineData("LogEntry", "log_entry")]
    [InlineData("Tenant", "tenant")]
    [InlineData("DurationMs", "duration_ms")]
    [InlineData("IPAddress", "ip_address")]
    [InlineData("Address2", "address2")]
    [InlineData("logEntries", "log_entries")]
    [InlineData("", "")]
    public void ToSnakeCase_Always_ConvertsPascalAndCamelCase(string input, string expected)
    {
        SnakeCaseNamer.ToSnakeCase(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("IsActive", "Active")]
    [InlineData("HasErrors", "Errors")]
    [InlineData("Island", "Island")]
    [InlineData("Hash", "Hash")]
    [InlineData("Enabled", "Enabled")]
    public void StripBooleanPrefix_Always_RemovesOnlyRealPrefixes(string input, string expected)
    {
        SnakeCaseNamer.StripBooleanPrefix(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("TenantId", "Tenant")]
    [InlineData("CreatedByUserId", "CreatedByUser")]
    [InlineData("Id", "Id")]
    [InlineData("Grid", "Grid")]
    public void StripIdSuffix_Always_RemovesTrailingId(string input, string expected)
    {
        SnakeCaseNamer.StripIdSuffix(input).Should().Be(expected);
    }
}
