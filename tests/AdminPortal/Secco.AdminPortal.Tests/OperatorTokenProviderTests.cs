using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Authorization;
using Secco.AdminPortal.Authentication;
using Xunit;

namespace Secco.AdminPortal.Tests;

/// <summary>
/// O <see cref="OperatorTokenProvider"/> lê o access token do principal do cookie
/// (ADR-0023) — a custódia server-side que alimenta as chamadas on-behalf-of.
/// </summary>
public class OperatorTokenProviderTests
{
    private sealed class StubAuthStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithTokenClaim_ReturnsTheToken()
    {
        var identity = new ClaimsIdentity(
            [new Claim(AdminPortalDefaults.AccessTokenClaim, "operator-access-token")], "test");
        var provider = new OperatorTokenProvider(new StubAuthStateProvider(new ClaimsPrincipal(identity)));

        (await provider.GetAccessTokenAsync()).Should().Be("operator-access-token");
    }

    [Fact]
    public async Task GetAccessTokenAsync_WithoutTokenClaim_ReturnsNull()
    {
        var provider = new OperatorTokenProvider(
            new StubAuthStateProvider(new ClaimsPrincipal(new ClaimsIdentity())));

        (await provider.GetAccessTokenAsync()).Should().BeNull();
    }
}
