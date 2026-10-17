using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Vínculo pessoa ↔ tenant no banco de plataforma (ADR-0040): nome por convention, chave natural,
/// vínculo do dono nascendo com a conta e remoção que leva só os perfis do tenant do vínculo.
/// </summary>
public class TenantMembershipSchemaTests(SecureGateApiFactory factory) : IClassFixture<SecureGateApiFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private SecureGateDbContext CreateContext() =>
		PlatformContextTestFactory.Create(SecureGateDatabaseProviderConfigurator.CreateOptions(
			SecureGateDatabaseProvider.SqlServer, factory.GetPlatformConnectionString()));

	[Fact]
	public async Task Tabela_Sempre_SegueANomenclaturaDaAdr0017()
	{
		await using var context = CreateContext();

		var columns = await context.Database
			.SqlQueryRaw<string>(
				"SELECT COLUMN_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'tb_tenant_memberships'")
			.ToListAsync();

		columns.Should().BeEquivalentTo(["id_pfk_user", "id_pfk_tenant", "fl_active", "dt_created_at"]);
	}

	[Fact]
	public async Task CriarUsuario_PeloDiretorio_GeraVinculoAtivoDoDono()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);

		using var scope = factory.Services.CreatePlatformScope();
		var directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
		var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>();

		var created = await directory.CreateAsync(
			new CreateUserData(tenantId, $"dono-{Guid.NewGuid():N}@teste.local", LocalLogin: true, Roles: []));

		created.IsSuccess.Should().BeTrue();
		var membership = await memberships.GetAsync(tenantId, created.Value.Id);
		membership.Should().NotBeNull();
		membership!.IsActive.Should().BeTrue();
		(await memberships.IsActiveMemberAsync(tenantId, created.Value.Id)).Should().BeTrue();
	}

	[Fact]
	public async Task GetAsync_NaoMembro_RetornaNuloEInativo()
	{
		using var scope = factory.Services.CreatePlatformScope();
		var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>();

		(await memberships.GetAsync(Guid.NewGuid(), Guid.NewGuid())).Should().BeNull();
		(await memberships.IsActiveMemberAsync(Guid.NewGuid(), Guid.NewGuid())).Should().BeFalse();
	}

	[Fact]
	public async Task IsActiveMemberAsync_VinculoSuspenso_RetornaFalso()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var userId = await IdentitySeed.UserAsync(factory, tenantId, $"susp-{Guid.NewGuid():N}@teste.local");

		await using (var seed = CreateContext())
		{
			var membership = new TenantMembership(userId, tenantId, DateTimeOffset.UtcNow);
			membership.Suspend();
			seed.TenantMemberships.Add(membership);
			await seed.SaveChangesAsync();
		}

		using var scope = factory.Services.CreatePlatformScope();
		var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>();

		(await memberships.GetAsync(tenantId, userId)).Should().NotBeNull();
		(await memberships.IsActiveMemberAsync(tenantId, userId)).Should().BeFalse();
	}

	[Fact]
	public async Task ChavePrimaria_VinculoDuplicado_EhRecusadaPeloBanco()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var userId = await IdentitySeed.UserAsync(factory, tenantId, $"dup-{Guid.NewGuid():N}@teste.local");

		await using var context = CreateContext();
		context.TenantMemberships.Add(new TenantMembership(userId, tenantId, DateTimeOffset.UtcNow));
		await context.SaveChangesAsync();

		await using var second = CreateContext();
		second.TenantMemberships.Add(new TenantMembership(userId, tenantId, DateTimeOffset.UtcNow));

		var act = () => second.SaveChangesAsync();

		await act.Should().ThrowAsync<DbUpdateException>();
	}

	[Fact]
	public async Task RemoveAsync_PessoaComPerfilEmDoisTenants_ApagaSoOsPerfisDoTenantDoVinculo()
	{
		var tenantA = await IdentitySeed.TenantAsync(factory);
		var tenantB = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantA, "leitor");
		await IdentitySeed.RoleAsync(factory, tenantB, "leitor");
		var userId = await IdentitySeed.UserAsync(factory, tenantA, $"multi-{Guid.NewGuid():N}@teste.local", "leitor");

		await using (var seed = CreateContext())
		{
			var roleB = await seed.Roles.SingleAsync(r => r.TenantId == tenantB && r.NormalizedName == "LEITOR");
			seed.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleB.Id });
			seed.TenantMemberships.AddRange(
				new TenantMembership(userId, tenantA, DateTimeOffset.UtcNow),
				new TenantMembership(userId, tenantB, DateTimeOffset.UtcNow));
			await seed.SaveChangesAsync();
		}

		using var scope = factory.Services.CreatePlatformScope();
		var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>();

		var removed = await memberships.RemoveAsync(tenantB, userId);

		removed.Should().BeTrue();
		await using var verify = CreateContext();
		(await verify.TenantMemberships.AsNoTracking().AnyAsync(m => m.UserId == userId && m.TenantId == tenantB))
			.Should().BeFalse();
		(await verify.TenantMemberships.AsNoTracking().AnyAsync(m => m.UserId == userId && m.TenantId == tenantA))
			.Should().BeTrue("o vínculo do outro tenant não é tocado");

		var remainingRoleTenants = await (
			from userRole in verify.UserRoles.AsNoTracking()
			join role in verify.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where userRole.UserId == userId
			select role.TenantId).ToListAsync();
		remainingRoleTenants.Should().Equal(tenantA);
	}

	[Fact]
	public async Task RemoveAsync_NaoMembro_RetornaFalso()
	{
		using var scope = factory.Services.CreatePlatformScope();
		var memberships = scope.ServiceProvider.GetRequiredService<ITenantMembershipRepository>();

		(await memberships.RemoveAsync(Guid.NewGuid(), Guid.NewGuid())).Should().BeFalse();
	}
}

/// <summary>
/// Migration <c>AddTenantMemberships</c> com dado: usuário que já existia antes ganha o vínculo
/// ativo com o próprio tenant (ADR-0040) — "quem tem um tenant só não muda nada".
/// </summary>
public class TenantMembershipMigrationTests
{
	private const string PreviousMigration = "20261010214427_AddPermissionCatalog";

	[Fact]
	public async Task Migration_UsuarioExistente_GanhaVinculoAtivoDoDono()
	{
		var factory = new SecureGateApiFactory();
		await factory.InitializeAsync();   // sobe o SQL Server; NÃO migra

		try
		{
			var connectionString = factory.GetConnectionStringFor("secco_membership_migration");

			await using var context = PlatformContextTestFactory.Create(SecureGateDatabaseProviderConfigurator.CreateOptions(
				SecureGateDatabaseProvider.SqlServer, connectionString));
			var migrator = context.GetService<IMigrator>();

			// Estado anterior à migration, com um usuário já cadastrado
			await migrator.MigrateAsync(PreviousMigration);

			var tenant = new Tenant("Tenant pre-vinculo", $"pre-{Guid.NewGuid():N}"[..20]);
			var user = new User
			{
				Id = Guid.CreateVersion7(),
				UserName = "antigo@teste.local",
				NormalizedUserName = "ANTIGO@TESTE.LOCAL",
				TenantId = tenant.Id,
				SecurityStamp = Guid.NewGuid().ToString(),
			};
			context.Tenants.Add(tenant);
			context.Users.Add(user);
			await context.SaveChangesAsync();

			await migrator.MigrateAsync();

			var memberships = await context.TenantMemberships.AsNoTracking()
				.Where(m => m.UserId == user.Id).ToListAsync();
			memberships.Should().ContainSingle();
			memberships[0].TenantId.Should().Be(tenant.Id);
			memberships[0].IsActive.Should().BeTrue();
			memberships[0].CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}
}
