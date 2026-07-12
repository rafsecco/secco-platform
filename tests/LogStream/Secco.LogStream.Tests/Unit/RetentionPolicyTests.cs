using FluentAssertions;
using Secco.LogStream.Infrastructure.Retention;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class RetentionPolicyTests
{
    [Fact]
    public void ResolveDays_WithTenantOverride_WinsOverDefault()
    {
        var tenantId = Guid.NewGuid();
        var options = new LogStreamRetentionOptions { DefaultDays = 30 };
        options.DaysByTenant[tenantId] = 90;

        RetentionPolicy.ResolveDays(options, tenantId).Should().Be(90);
        RetentionPolicy.ResolveDays(options, Guid.NewGuid()).Should().Be(30);
    }

    [Fact]
    public void ResolveDays_WithoutAnyConfiguration_ReturnsNull()
    {
        RetentionPolicy.ResolveDays(new LogStreamRetentionOptions(), Guid.NewGuid())
            .Should().BeNull("sem opt-in explícito, nada é expurgado");
    }

    [Theory]
    [InlineData(0, 6, false)]
    [InlineData(-5, 6, false)]
    [InlineData(30, 0, false)]
    [InlineData(30, 6, true)]
    [InlineData(null, 6, true)]
    public void IsValid_Always_RejectsNonPositiveValues(int? defaultDays, int intervalHours, bool expected)
    {
        var options = new LogStreamRetentionOptions { DefaultDays = defaultDays, IntervalHours = intervalHours };

        RetentionPolicy.IsValid(options).Should().Be(expected);
    }

    [Fact]
    public void IsValid_WithNonPositiveTenantOverride_ReturnsFalse()
    {
        var options = new LogStreamRetentionOptions { DefaultDays = 30 };
        options.DaysByTenant[Guid.NewGuid()] = 0;

        RetentionPolicy.IsValid(options).Should().BeFalse("config inválida jamais expurga (fail-safe)");
    }
}
