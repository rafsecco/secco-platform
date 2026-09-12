using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Convergência de vocabulário do role de operador (issue #4/2026-09): o nome mudou de
/// <c>platform-operator</c> para <see cref="SecureGatePlatform.OperatorRole"/>
/// (<c>installation-operator</c>), mas uma instalação existente já tem o role LEGADO gravado
/// em <c>tb_roles</c> com usuários atribuídos em <c>tb_user_roles</c>. O seed de referência
/// precisa RENOMEAR no lugar — preservando o Id — em vez de criar um role novo e deixar o
/// antigo (e as atribuições que apontam para ele) órfão.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class OperatorRoleConvergenceTests(SecureGateApiFactory secureGate) : IAsyncLifetime
{
	private static readonly string LegacyNormalized = SecureGatePlatform.LegacyOperatorRole.ToUpperInvariant();
	private static readonly string CurrentNormalized = SecureGatePlatform.OperatorRole.ToUpperInvariant();

	public Task InitializeAsync() => secureGate.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task SeedInstallationOperator_RoleComNomeLegado_RenomeiaNoLugarPreservandoIdEAtribuicao()
	{
		var (roleId, userId) = await SeedLegacyOperatorRoleWithUserAsync();

		await secureGate.Services.SeedSeccoDataAsync();

		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var rolesInTenant = await context.Roles
			.Where(r => r.TenantId == SecureGatePlatform.TenantId
				&& (r.NormalizedName == CurrentNormalized || r.NormalizedName == LegacyNormalized))
			.ToListAsync();

		// (d) não existem dois papéis — a convergência renomeia no lugar, nunca duplica
		rolesInTenant.Should().HaveCount(1,
			"a convergência renomeia o role legado no lugar em vez de criar um segundo");

		var converged = rolesInTenant.Single();

		// (a) o papel passou a ter o nome novo
		converged.Name.Should().Be(SecureGatePlatform.OperatorRole);
		converged.NormalizedName.Should().Be(CurrentNormalized);

		// (b) o Id é o mesmo de antes da convergência
		converged.Id.Should().Be(roleId,
			"preservar o Id é o que mantém as atribuições em tb_user_roles válidas");

		// (c) a atribuição do usuário ao role (pelo Id, nunca pelo nome) continua válida
		var assignmentStillValid = await context.UserRoles
			.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
		assignmentStillValid.Should().BeTrue(
			"a atribuição referencia o Id do role, que a convergência preserva");
	}

	[Fact]
	public async Task SeedInstallationOperator_RodadoNovamenteAposConvergencia_NaoAlteraNada()
	{
		var (roleId, _) = await SeedLegacyOperatorRoleWithUserAsync();

		// Primeira execução: converge o nome legado para o novo
		await secureGate.Services.SeedSeccoDataAsync();

		string stampAfterConvergence;
		using (var scope = secureGate.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			stampAfterConvergence = await context.Roles
				.Where(r => r.Id == roleId)
				.Select(r => r.ConcurrencyStamp!)
				.SingleAsync();
		}

		// Segunda execução: já convergido — não deve escrever nada (ADR-0019, seed de
		// referência roda em todos os ambientes, então precisa ser seguro repetir)
		await secureGate.Services.SeedSeccoDataAsync();

		using var verifyScope = secureGate.Services.CreateScope();
		var verifyContext = verifyScope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var afterSecondRun = await verifyContext.Roles.SingleAsync(r => r.Id == roleId);

		afterSecondRun.Name.Should().Be(SecureGatePlatform.OperatorRole);
		afterSecondRun.ConcurrencyStamp.Should().Be(stampAfterConvergence,
			"rodar o seed de novo depois de convergido não deve escrever nada (idempotência)");

		var rolesInTenant = await verifyContext.Roles
			.CountAsync(r => r.TenantId == SecureGatePlatform.TenantId
				&& (r.NormalizedName == CurrentNormalized || r.NormalizedName == LegacyNormalized));
		rolesInTenant.Should().Be(1, "a segunda execução também não duplica o role");
	}

	/// <summary>
	/// Simula uma instalação anterior: renomeia o role de operador (já semeado com o nome
	/// atual pela migração da factory) de volta para o nome LEGADO, no MESMO Id, e atribui um
	/// usuário a ele — exatamente o estado que a convergência do seed precisa saber resolver.
	/// </summary>
	private async Task<(Guid RoleId, Guid UserId)> SeedLegacyOperatorRoleWithUserAsync()
	{
		using var scope = secureGate.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

		var role = await context.Roles.SingleAsync(
			r => r.TenantId == SecureGatePlatform.TenantId && r.NormalizedName == CurrentNormalized);

		role.Name = SecureGatePlatform.LegacyOperatorRole;
		role.NormalizedName = LegacyNormalized;
		await context.SaveChangesAsync();

		var email = $"legacy-op-{Guid.NewGuid():N}@secco.test";
		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = SecureGatePlatform.TenantId,
			UserName = email,
			Email = email,
			EmailConfirmed = true,
		};
		(await userManager.CreateAsync(user, "Legacy@0perator!")).Succeeded.Should().BeTrue();

		context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
		await context.SaveChangesAsync();

		return (role.Id, user.Id);
	}
}
