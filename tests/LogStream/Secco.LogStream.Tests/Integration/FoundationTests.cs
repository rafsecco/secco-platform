using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Secco.LogStream.Infrastructure;
using Secco.LogStream.Infrastructure.Contexts;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

public class FoundationTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>
{
    [Fact]
    public async Task HealthLive_OnFoundation_ReturnsOk()
    {
        var response = await factory.CreateClient().GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_OnFoundation_ReturnsOk()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TenantDatabases_AfterMigration_ConnectAndAreIsolated()
    {
        // O helper itera o catálogo (dois tenants apontando para bancos distintos do container)
        await factory.Services.MigrateLogStreamTenantDatabasesAsync();

        await using var contextAlfa = CreateContext("secco_logstream_alfa");
        await using var contextBeta = CreateContext("secco_logstream_beta");

        (await contextAlfa.Database.CanConnectAsync()).Should().BeTrue();
        (await contextBeta.Database.CanConnectAsync()).Should().BeTrue();

        var databaseAlfa = contextAlfa.Database.GetDbConnection().Database;
        var databaseBeta = contextBeta.Database.GetDbConnection().Database;

        databaseAlfa.Should().NotBe(databaseBeta, "cada tenant possui banco próprio (ADR-0005)");
    }

    [Fact]
    public async Task TenantDatabases_AfterMigration_HaveAllMigrationsApplied()
    {
        await factory.Services.MigrateLogStreamTenantDatabasesAsync();

        await using var context = CreateContext("secco_logstream_alfa");

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await context.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_Init"));
    }

    private LogStreamDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<LogStreamDbContext>()
            .UseSqlServer(factory.GetTenantConnectionString(databaseName))
            .Options);
}
