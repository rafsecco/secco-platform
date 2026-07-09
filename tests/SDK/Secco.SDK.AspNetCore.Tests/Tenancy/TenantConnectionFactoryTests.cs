using FluentAssertions;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Tenancy;

public class TenantConnectionFactoryTests
{
    private sealed class FakeTenantContext(Guid? tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;

        public bool IsResolved => tenantId is not null;
    }

    private sealed class FakeTenantCatalog(TenantInfo? tenant) : ITenantCatalog
    {
        public ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(tenant);
    }

    [Fact]
    public async Task GetConnectionStringAsync_WithResolvedTenantInCatalog_ReturnsConnectionString()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TenantConnectionFactory(
            new FakeTenantContext(tenantId),
            new FakeTenantCatalog(new TenantInfo(tenantId, "Server=sql;Database=tenant_a;")));

        var connectionString = await factory.GetConnectionStringAsync();

        connectionString.Should().Be("Server=sql;Database=tenant_a;");
    }

    [Fact]
    public async Task GetConnectionStringAsync_WithoutResolvedTenant_ThrowsTenantNotResolvedException()
    {
        var factory = new TenantConnectionFactory(
            new FakeTenantContext(null),
            new FakeTenantCatalog(new TenantInfo(Guid.NewGuid(), "Server=sql;")));

        var act = () => factory.GetConnectionStringAsync().AsTask();

        await act.Should().ThrowAsync<TenantNotResolvedException>();
    }

    [Fact]
    public async Task GetConnectionStringAsync_WhenTenantNotInCatalog_ThrowsTenantNotFoundException()
    {
        var tenantId = Guid.NewGuid();
        var factory = new TenantConnectionFactory(
            new FakeTenantContext(tenantId),
            new FakeTenantCatalog(null));

        var act = () => factory.GetConnectionStringAsync().AsTask();

        (await act.Should().ThrowAsync<TenantNotFoundException>())
            .Which.TenantId.Should().Be(tenantId);
    }
}
