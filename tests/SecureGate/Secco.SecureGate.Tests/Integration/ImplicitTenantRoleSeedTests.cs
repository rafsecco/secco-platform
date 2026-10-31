using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Seed de referência do perfil implícito (ADR-0056): cria nos tenants que já existem, é idempotente, nunca cria no
/// tenant de plataforma e RECUSA — sem converter nada — quando um tenant já tem um perfil comum com o nome reservado.
/// </summary>
[Collection(ReseedApiCollectionDefinition.Name)]
public class ImplicitTenantRoleSeedTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly string Normalized = SecureGatePlatform.TenantMemberRole.ToUpperInvariant();

	public Task InitializeAsync() => factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private async Task<List<Infrastructure.Identity.Role>> RolesOfAsync(Guid tenantId)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.Roles.AsNoTracking().Where(r => r.TenantId == tenantId && r.NormalizedName == Normalized).ToListAsync();
	}

	[Fact]
	public async Task Seed_CriaNosTenantsExistentes_EhIdempotente_ENuncaNoDePlataforma()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		(await RolesOfAsync(tenantId)).Should().BeEmpty("o tenant foi montado direto no banco, sem o perfil");

		await factory.Services.SeedSeccoDataAsync();
		var first = await RolesOfAsync(tenantId);
		await factory.Services.SeedSeccoDataAsync();
		var second = await RolesOfAsync(tenantId);

		first.Should().ContainSingle().Which.IsImplicit.Should().BeTrue();
		second.Should().ContainSingle().Which.Id.Should().Be(first[0].Id, "rodar de novo não troca nem duplica");
		(await RolesOfAsync(SecureGatePlatform.TenantId)).Should().BeEmpty("o tenant de plataforma nunca tem o perfil");
	}

	[Fact]
	public async Task Seed_NaoMexeNasPermissoesDeUmImplicitoJaEditado()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.ImplicitRoleAsync(factory, tenantId, "documentos:read");

		await factory.Services.SeedSeccoDataAsync();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var role = await context.Roles.AsNoTracking().SingleAsync(r => r.TenantId == tenantId && r.NormalizedName == Normalized);
		(await context.RoleClaims.AsNoTracking().Where(c => c.RoleId == role.Id).Select(c => c.ClaimValue).ToListAsync())
			.Should().Equal("documentos:read");
	}

	[Fact]
	public async Task Seed_ComPerfilComumHomonimo_FalhaListandoOTenant_SemConverterNada()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantId, SecureGatePlatform.TenantMemberRole, "documentos:write");

		try
		{
			var seed = () => factory.Services.SeedSeccoDataAsync();

			(await seed.Should().ThrowAsync<InvalidOperationException>())
				.Which.Message.Should().Contain(tenantId.ToString());

			var roles = await RolesOfAsync(tenantId);
			roles.Should().ContainSingle().Which.IsImplicit.Should().BeFalse("converter daria as permissões do perfil antigo a todos");
		}
		finally
		{
			// A base é compartilhada: um homônimo esquecido derrubaria o seed de todo teste seguinte
			using var scope = factory.Services.CreatePlatformScope();
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			var role = await context.Roles.SingleAsync(r => r.TenantId == tenantId && r.NormalizedName == Normalized);
			context.RoleClaims.RemoveRange(await context.RoleClaims.Where(c => c.RoleId == role.Id).ToListAsync());
			context.Roles.Remove(role);
			await context.SaveChangesAsync();
		}
	}
}
