using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Secco.SDK.AspNetCore.Authorization;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Authorization;

/// <summary>Resolver por configuração + cache obrigatório da ADR-0021 (estrito, fail-closed).</summary>
public class PermissionResolutionTests
{
    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => (string?)e.Value))
            .Build();

    [Fact]
    public async Task ConfigurationResolver_ReadsPermissionsOfRole()
    {
        var resolver = new ConfigurationPermissionResolver(BuildConfiguration(
            ("Secco:Authorization:Roles:writer:Permissions:0", "log-entries:write"),
            ("Secco:Authorization:Roles:writer:Permissions:1", "log-entries:read")));

        var permissions = await resolver.ResolveAsync(Guid.NewGuid(), "writer");

        permissions.Should().BeEquivalentTo(["log-entries:write", "log-entries:read"]);
    }

    [Fact]
    public async Task ConfigurationResolver_IgnoresMalformedEntries()
    {
        var resolver = new ConfigurationPermissionResolver(BuildConfiguration(
            ("Secco:Authorization:Roles:writer:Permissions:0", "SemFormato"),
            ("Secco:Authorization:Roles:writer:Permissions:1", "log-entries:write")));

        var permissions = await resolver.ResolveAsync(Guid.NewGuid(), "writer");

        permissions.Should().BeEquivalentTo(["log-entries:write"],
            "entradas fora do formato canônico não entram no conjunto");
    }

    [Fact]
    public async Task ConfigurationResolver_UnknownRole_ReturnsEmpty()
    {
        var resolver = new ConfigurationPermissionResolver(BuildConfiguration());

        (await resolver.ResolveAsync(Guid.NewGuid(), "fantasma")).Should().BeEmpty();
    }

    private sealed class CountingResolver : IPermissionResolver
    {
        public int Calls { get; private set; }

        public bool Fail { get; set; }

        public ValueTask<IReadOnlySet<string>> ResolveAsync(
            Guid tenantId, string role, CancellationToken cancellationToken = default)
        {
            Calls++;

            return Fail
                ? throw new HttpRequestException("indisponível")
                : ValueTask.FromResult<IReadOnlySet<string>>(new HashSet<string> { "log-entries:read" });
        }
    }

    private static CachedPermissionResolver BuildCache(IPermissionResolver inner, int ttlSeconds = 300) =>
        new(inner, Options.Create(new SeccoAuthorizationOptions { CacheTtlSeconds = ttlSeconds }));

    [Fact]
    public async Task Cache_WithinTtl_DoesNotCallInnerAgain()
    {
        var inner = new CountingResolver();
        var cache = BuildCache(inner);
        var tenantId = Guid.NewGuid();

        await cache.ResolveAsync(tenantId, "leitor");
        await cache.ResolveAsync(tenantId, "leitor");

        inner.Calls.Should().Be(1, "a chave (tenant, role) está fresca no cache");
    }

    [Fact]
    public async Task Cache_KeysByTenantAndRole()
    {
        var inner = new CountingResolver();
        var cache = BuildCache(inner);

        await cache.ResolveAsync(Guid.NewGuid(), "leitor");
        await cache.ResolveAsync(Guid.NewGuid(), "leitor");

        inner.Calls.Should().Be(2, "tenants diferentes não compartilham entrada (ADR-0021)");
    }

    [Fact]
    public async Task Cache_ExpiredWithResolverDown_PropagatesFailure()
    {
        var inner = new CountingResolver();
        var cache = BuildCache(inner, ttlSeconds: 1);
        var tenantId = Guid.NewGuid();

        await cache.ResolveAsync(tenantId, "leitor");

        inner.Fail = true;
        await Task.Delay(TimeSpan.FromSeconds(1.2));

        var act = async () => await cache.ResolveAsync(tenantId, "leitor");

        // Estrito e fail-closed (ADR-0021): NADA de stale — o handler nega o acesso
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
