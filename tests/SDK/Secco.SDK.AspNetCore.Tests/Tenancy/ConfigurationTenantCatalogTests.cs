using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.SDK.AspNetCore.Tests.Tenancy;

public class ConfigurationTenantCatalogTests
{
    private static ConfigurationTenantCatalog CreateCatalog(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());

    [Fact]
    public async Task FindAsync_WhenTenantConfigured_ReturnsTenantInfo()
    {
        var tenantId = Guid.NewGuid();
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            [$"Secco:Tenancy:Tenants:{tenantId}:ConnectionString"] = "Server=sql;Database=tenant_a;",
        });

        var tenant = await catalog.FindAsync(tenantId);

        tenant.Should().NotBeNull();
        tenant.TenantId.Should().Be(tenantId);
        tenant.ConnectionString.Should().Be("Server=sql;Database=tenant_a;");
    }

    [Fact]
    public async Task FindAsync_WhenTenantMissing_ReturnsNull()
    {
        var catalog = CreateCatalog([]);

        var tenant = await catalog.FindAsync(Guid.NewGuid());

        tenant.Should().BeNull();
    }

    [Fact]
    public async Task FindAsync_WhenConnectionStringEmpty_ReturnsNull()
    {
        var tenantId = Guid.NewGuid();
        var catalog = CreateCatalog(new Dictionary<string, string?>
        {
            [$"Secco:Tenancy:Tenants:{tenantId}:ConnectionString"] = "  ",
        });

        var tenant = await catalog.FindAsync(tenantId);

        tenant.Should().BeNull();
    }
}
