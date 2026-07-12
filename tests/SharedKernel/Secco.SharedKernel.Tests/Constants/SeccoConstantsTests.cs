using FluentAssertions;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SharedKernel.Tests.Constants;

public class SeccoConstantsTests
{
    [Fact]
    public void SeccoClaims_Always_MatchAdr0007StandardizedNames()
    {
        SeccoClaims.Subject.Should().Be("sub");
        SeccoClaims.TenantId.Should().Be("tenant_id");
        SeccoClaims.Role.Should().Be("role");
        SeccoClaims.Scope.Should().Be("scope");
    }

    [Fact]
    public void SeccoHeaders_Always_MatchPlatformStandardizedNames()
    {
        SeccoHeaders.CorrelationId.Should().Be("X-Correlation-Id");
        SeccoHeaders.TenantId.Should().Be("X-Tenant-Id");
    }
}
