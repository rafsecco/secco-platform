using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Prova da decisão de schema (ADR-0017 completa no IAM): tabelas do Identity/OpenIddict
/// re-nomeadas (<c>tb_users</c>, <c>tb_oidc_applications</c>...) e colunas derivadas pela
/// <c>SeccoNamingConvention</c> (<c>fl_email_confirmed</c>, <c>id_pk_user</c>...).
/// </summary>
public class PlatformSchemaTests(SecureGateApiFactory factory) : IClassFixture<SecureGateApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private SecureGateDbContext CreateContext() =>
        new(SecureGateDatabaseProviderConfigurator.CreateOptions(
            SecureGateDatabaseProvider.SqlServer, factory.GetPlatformConnectionString()));

    [Fact]
    public async Task Migrations_Always_ApplyFromScratch()
    {
        await using var context = CreateContext();

        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task IdentityAndOidcTables_Always_FollowAdr0017Naming()
    {
        await using var context = CreateContext();

        var tables = await context.Database
            .SqlQueryRaw<string>("SELECT TABLE_NAME AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'")
            .ToListAsync();

        tables.Should().Contain(["tb_users", "tb_roles", "tb_user_roles", "tb_role_claims",
            "tb_oidc_applications", "tb_oidc_authorizations", "tb_oidc_scopes", "tb_oidc_tokens",
            "tb_tenants"]);

        tables.Should().NotContain(t => t.StartsWith("AspNet") || t.StartsWith("OpenIddict"),
            "as tabelas de framework são re-nomeadas para a convenção (ADR-0017 completa)");
    }

    [Fact]
    public async Task IdentityColumns_Always_ComeFromTheSeccoNamingConvention()
    {
        await using var context = CreateContext();

        var columns = await context.Database
            .SqlQueryRaw<string>("SELECT COLUMN_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'tb_users'")
            .ToListAsync();

        columns.Should().Contain(["id_pk_user", "id_fk_tenant", "ds_user_name", "ds_password_hash",
            "fl_email_confirmed", "fl_two_factor_enabled", "nr_access_failed_count"]);
    }

    [Fact]
    public async Task UserRoleJoinTable_Always_UsesPfkColumns()
    {
        await using var context = CreateContext();

        var columns = await context.Database
            .SqlQueryRaw<string>("SELECT COLUMN_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'tb_user_roles'")
            .ToListAsync();

        columns.Should().BeEquivalentTo(["id_pfk_user", "id_pfk_role"],
            "PK composta cujos membros são FKs usa id_pfk_ (ADR-0017)");
    }

    [Fact]
    public async Task TenantAndUser_Always_RoundTripThroughTheRenamedSchema()
    {
        var slug = $"smoke-{Guid.NewGuid():N}"[..20];

        await using (var seed = CreateContext())
        {
            var tenant = new Tenant("Tenant de Smoke", slug);
            seed.Tenants.Add(tenant);
            seed.Users.Add(new User
            {
                Id = Guid.CreateVersion7(),
                UserName = $"user-{slug}",
                NormalizedUserName = $"USER-{slug.ToUpperInvariant()}",
                TenantId = tenant.Id,
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            await seed.SaveChangesAsync();
        }

        await using var verify = CreateContext();

        var stored = await verify.Users.AsNoTracking()
            .SingleAsync(u => u.UserName == $"user-{slug}");
        (await verify.Tenants.AsNoTracking().AnyAsync(t => t.Id == stored.TenantId && t.Slug == slug))
            .Should().BeTrue("usuário referencia o tenant do catálogo (ADR-0022)");
    }

    [Fact]
    public async Task HealthEndpoints_Always_RespondAnonymously()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
