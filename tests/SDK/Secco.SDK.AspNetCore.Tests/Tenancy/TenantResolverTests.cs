using FluentAssertions;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Tenancy;

public class TenantResolverTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    [Fact]
    public void Resolve_WithValidClaimOnly_ResolvesFromClaim()
    {
        var resolution = TenantResolver.Resolve(TenantA.ToString(), headerValue: null);

        resolution.TenantId.Should().Be(TenantA);
        resolution.IsConflict.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WithValidHeaderOnly_ResolvesFromHeader()
    {
        var resolution = TenantResolver.Resolve(claimValue: null, TenantA.ToString());

        resolution.TenantId.Should().Be(TenantA);
        resolution.IsConflict.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WhenClaimAndHeaderMatch_ResolvesWithoutConflict()
    {
        var resolution = TenantResolver.Resolve(TenantA.ToString(), TenantA.ToString());

        resolution.TenantId.Should().Be(TenantA);
        resolution.IsConflict.Should().BeFalse();
    }

    [Fact]
    public void Resolve_WhenClaimAndHeaderDiverge_ReturnsConflict()
    {
        var resolution = TenantResolver.Resolve(TenantA.ToString(), TenantB.ToString());

        resolution.IsConflict.Should().BeTrue();
        resolution.TenantId.Should().BeNull();
    }

    [Fact]
    public void Resolve_WithValidClaimAndMalformedHeader_ReturnsConflict()
    {
        var resolution = TenantResolver.Resolve(TenantA.ToString(), "not-a-guid");

        resolution.IsConflict.Should().BeTrue();
    }

    [Fact]
    public void Resolve_WithInvalidClaim_DoesNotFallBackToHeader()
    {
        var resolution = TenantResolver.Resolve("not-a-guid", TenantA.ToString());

        resolution.TenantId.Should().BeNull();
        resolution.IsConflict.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(null, "not-a-guid")]
    [InlineData(null, "00000000-0000-0000-0000-000000000000")]
    public void Resolve_WithoutAnyValidSource_ReturnsUnresolved(string? claimValue, string? headerValue)
    {
        var resolution = TenantResolver.Resolve(claimValue, headerValue);

        resolution.TenantId.Should().BeNull();
        resolution.IsConflict.Should().BeFalse();
    }
}
