using FluentAssertions;
using Secco.SDK.AspNetCore.Correlation;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Correlation;

public class CorrelationIdParserTests
{
    [Fact]
    public void TryParse_WithValidGuid_ReturnsTrueAndParsedValue()
    {
        var guid = Guid.NewGuid();

        var result = CorrelationIdParser.TryParse(guid.ToString(), out var parsed);

        result.Should().BeTrue();
        parsed.Should().Be(guid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void TryParse_WithInvalidValue_ReturnsFalseAndEmptyGuid(string? value)
    {
        var result = CorrelationIdParser.TryParse(value, out var parsed);

        result.Should().BeFalse();
        parsed.Should().Be(Guid.Empty);
    }
}
