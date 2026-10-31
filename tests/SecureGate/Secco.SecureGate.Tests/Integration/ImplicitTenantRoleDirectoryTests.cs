using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O ponto único que monta os papéis de token de pessoa (ADR-0056) guarda as próprias condições, sem depender de
/// quem chama: o login já confere o vínculo e entra no escopo do tenant, mas esta é a segunda barreira. Por isso os
/// casos rodam em modo PLATAFORMA, onde nem o filtro nem a RLS restringem a consulta.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ImplicitTenantRoleDirectoryTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private const string Implicit = SecureGatePlatform.TenantMemberRole;

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email(string prefix) => $"{prefix}-{Guid.NewGuid():N}@secco.test";

	private async Task<IReadOnlyList<string>> RolesAsync(Guid userId, Guid tenantId)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return await scope.ServiceProvider.GetRequiredService<IUserDirectory>().GetRolesInTenantAsync(userId, tenantId);
	}

	private async Task MembershipAsync(Guid userId, Guid tenantId, bool suspended)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var membership = new TenantMembership(userId, tenantId, DateTimeOffset.UtcNow);

		if (suspended)
		{
			membership.Suspend();
		}

		context.TenantMemberships.Add(membership);
		await context.SaveChangesAsync();
	}

	private async Task<Guid> ImplicitTenantAsync()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.ImplicitRoleAsync(factory, tenantId, "manuais:read");

		return tenantId;
	}

	[Fact]
	public async Task ContaDona_E_MembroAtivo_LevamOImplicito()
	{
		var tenantA = await ImplicitTenantAsync();
		var tenantB = await ImplicitTenantAsync();
		var userId = await IdentitySeed.UserAsync(factory, tenantA, Email("dono"));
		await MembershipAsync(userId, tenantB, suspended: false);

		(await RolesAsync(userId, tenantA)).Should().Contain(Implicit);
		(await RolesAsync(userId, tenantB)).Should().Contain(Implicit);
	}

	[Fact]
	public async Task SemVinculoComOTenant_NaoLevaOImplicitoDele()
	{
		var tenantA = await ImplicitTenantAsync();
		var tenantB = await ImplicitTenantAsync();
		var userId = await IdentitySeed.UserAsync(factory, tenantA, Email("estranho"));

		(await RolesAsync(userId, tenantB)).Should().NotContain(Implicit);
	}

	[Fact]
	public async Task VinculoSuspenso_NaoLevaOImplicito()
	{
		var tenantA = await ImplicitTenantAsync();
		var tenantB = await ImplicitTenantAsync();
		var userId = await IdentitySeed.UserAsync(factory, tenantA, Email("suspenso"));
		await MembershipAsync(userId, tenantB, suspended: true);

		(await RolesAsync(userId, tenantB)).Should().NotContain(Implicit);
	}

	// Em modo plataforma a consulta enxerga os implícitos de todos os tenants: só a condição de tenant da própria
	// consulta impede que o dono do tenant de plataforma receba o perfil de outro tenant
	[Fact]
	public async Task DonoDoTenantDePlataforma_NuncaLevaOImplicito_MesmoComOutrosTenantsTendoOPerfil()
	{
		await ImplicitTenantAsync();
		var email = Email("operador");
		await IdentitySeed.PlatformOperatorAsync(factory, email);

		Guid operatorId;

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email);
			user!.TenantId.Should().Be(SecureGatePlatform.TenantId);
			operatorId = user.Id;
		}

		(await RolesAsync(operatorId, SecureGatePlatform.TenantId)).Should().NotContain(Implicit);
	}
}
