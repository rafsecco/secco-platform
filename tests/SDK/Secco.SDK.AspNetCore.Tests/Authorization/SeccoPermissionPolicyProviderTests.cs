using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Secco.SDK.AspNetCore.Authorization;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Authorization;

/// <summary>Policies dinâmicas por permissão (ADR-0021) sem quebrar o provider padrão.</summary>
public class SeccoPermissionPolicyProviderTests
{
    private static SeccoPermissionPolicyProvider BuildProvider(AuthorizationOptions? options = null) =>
        new(Options.Create(options ?? new AuthorizationOptions()));

    [Fact]
    public async Task GetPolicy_WithPermissionShapedName_BuildsPermissionRequirement()
    {
        var policy = await BuildProvider().GetPolicyAsync("log-entries:write");

        policy.Should().NotBeNull();
        policy.Requirements.OfType<PermissionRequirement>()
            .Should().ContainSingle(r => r.Permission == "log-entries:write");
    }

    [Fact]
    public async Task GetPolicy_WithRegularName_DelegatesToDefaultProvider()
    {
        var options = new AuthorizationOptions();
        options.AddPolicy("PolicyNomeada", policy => policy.RequireAuthenticatedUser());

        var named = await BuildProvider(options).GetPolicyAsync("PolicyNomeada");
        var unknown = await BuildProvider(options).GetPolicyAsync("Inexistente");

        named.Should().NotBeNull("nomes fora do formato canônico seguem o provider padrão");
        unknown.Should().BeNull();
    }

    [Fact]
    public async Task GetFallbackPolicy_PreservesFailClosedFallback()
    {
        var options = new AuthorizationOptions
        {
            FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build(),
        };

        var fallback = await BuildProvider(options).GetFallbackPolicyAsync();

        fallback.Should().NotBeNull("a FallbackPolicy fail-closed da plataforma segue intacta");
    }
}
