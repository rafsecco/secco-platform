using FluentAssertions;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Xunit;

namespace Secco.LogStream.Tests.Unit;

public class ProcessStatusRuleTests
{
    [Fact]
    public void FromMaxLevel_WithoutDetails_ReturnsSuccess()
    {
        ProcessStatusRule.FromMaxLevel(null).Should().Be(ProcessStatus.Success);
    }

    [Theory]
    [InlineData(LogEntryLevel.Trace, ProcessStatus.Success)]
    [InlineData(LogEntryLevel.Debug, ProcessStatus.Success)]
    [InlineData(LogEntryLevel.Information, ProcessStatus.Success)]
    [InlineData(LogEntryLevel.Warning, ProcessStatus.Warning)]
    [InlineData(LogEntryLevel.Error, ProcessStatus.Error)]
    [InlineData(LogEntryLevel.Critical, ProcessStatus.Critical)]
    public void FromMaxLevel_Always_MapsWorstLevelToStatus(LogEntryLevel maxLevel, ProcessStatus expected)
    {
        ProcessStatusRule.FromMaxLevel(maxLevel).Should().Be(expected);
    }
}
