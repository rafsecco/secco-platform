using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Scheduling;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Federation;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Ciclo periódico de reconciliação (ADR-0055, entrega A), contra o banco real e um Graph falso: o lease no banco de
/// plataforma garante uma execução por intervalo, cada tenant é isolado dos outros, e a leitura que falha não muda
/// nada. O serviço é conduzido por <c>RunOnceAsync</c>, como o da retenção (ADR-0054).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class DirectorySyncCycleTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		// O lease é global à instalação e a suíte compartilha o banco: cada teste começa sem ele
		using var scope = factory.Services.CreatePlatformScope();
		await scope.ServiceProvider.GetRequiredService<SecureGateDbContext>().BackgroundLeases
			.Where(l => l.Name == DirectorySyncService.TaskName).ExecuteDeleteAsync();
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private DirectorySyncService Service(TimeProvider? clock = null, bool configured = true, DirectorySyncOptions? options = null) => new(
		factory.Services.GetRequiredService<IServiceScopeFactory>(),
		Options.Create(options ?? new DirectorySyncOptions()),
		Options.Create(new SecureGateEntraIdOptions
		{
			ClientId = configured ? "client-id" : null,
			ClientSecret = configured ? "client-secret" : null,
		}),
		clock ?? TimeProvider.System,
		NullLogger<DirectorySyncService>.Instance);

	private sealed record Tenant1(Guid TenantId, Guid DirectoryId, Guid GroupId, Guid UserId, Guid ObjectId);

	/// <summary>Tenant com federação, um grupo mapeado ao perfil "financeiro" e uma conta já vinculada ao diretório.</summary>
	private async Task<Tenant1> TenantAsync(bool federationEnabled = true, bool mapped = true)
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantId, "financeiro");
		var userId = await IdentitySeed.UserAsync(factory, tenantId, $"ciclo-{Guid.NewGuid():N}@secco.test");
		var directoryId = Guid.NewGuid();
		var groupId = Guid.NewGuid();
		var objectId = Guid.NewGuid();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var federation = new TenantFederation(tenantId, directoryId);
		federation.SetEnabled(federationEnabled);
		context.TenantFederations.Add(federation);

		if (mapped)
		{
			var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == "FINANCEIRO");
			context.TenantGroupRoleMappings.Add(new TenantGroupRoleMapping(tenantId, groupId, "Grupo", role.Id));
		}

		await context.SaveChangesAsync();

		var user = (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString()))!;
		(await scope.ServiceProvider.GetRequiredService<UserManager<User>>()
			.AddLoginAsync(user, new UserLoginInfo(DirectoryLinkKey.Provider, DirectoryLinkKey.Build(directoryId, objectId), "Entra")))
			.Succeeded.Should().BeTrue();

		return new Tenant1(tenantId, directoryId, groupId, userId, objectId);
	}

	private async Task<string[]> RolesAsync(Tenant1 t)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where userRole.UserId == t.UserId && role.TenantId == t.TenantId
			select role.Name!)
			.ToArrayAsync();
	}

	private async Task<string[]> TrailActionsAsync(Tenant1 t)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.AuthorityAuditEntries.AsNoTracking()
			.Where(e => e.TenantId == t.TenantId && e.ActorType == "directory")
			.OrderBy(e => e.CreatedAt)
			.Select(e => e.Action)
			.ToArrayAsync();
	}

	// ---- Reconciliação do tenant inteiro ----

	[Fact]
	public async Task Ciclo_AcrescentaPerfilDeQuemEstaNoGrupo_EGravaATrilhaComTriggerCycle()
	{
		var t = await TenantAsync();
		factory.Graph.SetMembers(t.DirectoryId, t.GroupId, t.ObjectId);

		var run = await Service().RunOnceAsync();

		run.Ran.Should().BeTrue();
		(await RolesAsync(t)).Should().Equal("financeiro");
		(await TrailActionsAsync(t)).Should().Equal("sincronizacao-do-diretorio.perfil-acrescentado");
	}

	[Fact]
	public async Task Ciclo_RetiraPerfilDeQuemSaiuDoGrupo()
	{
		var t = await TenantAsync();
		await IdentitySeed.UserAsync(factory, t.TenantId, $"colega-{Guid.NewGuid():N}@secco.test", "financeiro");
		factory.Graph.SetMembers(t.DirectoryId, t.GroupId, t.ObjectId);
		await Service().RunOnceAsync();
		await ResetLeaseAsync();

		factory.Graph.SetMembers(t.DirectoryId, t.GroupId);
		await Service().RunOnceAsync();

		(await RolesAsync(t)).Should().BeEmpty();
		(await TrailActionsAsync(t)).Should().Equal(
			"sincronizacao-do-diretorio.perfil-acrescentado", "sincronizacao-do-diretorio.perfil-retirado");
	}

	[Fact]
	public async Task Ciclo_LeituraQueFalha_NaoMudaONadaDoTenant_ERegistra()
	{
		var t = await TenantAsync();
		factory.Graph.SetFailure(t.DirectoryId, t.GroupId, SecureGateErrors.Federation.DirectoryUnavailable);

		await Service().RunOnceAsync();

		(await RolesAsync(t)).Should().BeEmpty();
		(await TrailActionsAsync(t)).Should().Equal("sincronizacao-do-diretorio.leitura-falhou");
	}

	[Fact]
	public async Task Ciclo_FalhaNumTenant_NaoImpedeOsOutros()
	{
		var broken = await TenantAsync();
		var healthy = await TenantAsync();
		factory.Graph.ThrowFor(broken.DirectoryId);
		factory.Graph.SetMembers(healthy.DirectoryId, healthy.GroupId, healthy.ObjectId);

		var run = await Service().RunOnceAsync();

		run.Ran.Should().BeTrue();
		run.Failed.Should().BeGreaterThanOrEqualTo(1);
		(await RolesAsync(broken)).Should().BeEmpty();
		(await RolesAsync(healthy)).Should().Equal(["financeiro"], "o tenant com problema não pára o ciclo dos outros");
	}

	[Fact]
	public async Task Ciclo_MapeamentoRemovido_DesfazAConcessaoSemChamarOGraph()
	{
		var t = await TenantAsync(mapped: false);
		await IdentitySeed.UserAsync(factory, t.TenantId, $"colega-{Guid.NewGuid():N}@secco.test", "financeiro");
		await GrantDirectoryRoleAsync(t, Guid.NewGuid());

		await Service().RunOnceAsync();

		(await RolesAsync(t)).Should().BeEmpty("sem mapeamento nada mais justifica a atribuição, e ela não sai à mão");
		factory.Graph.CallsFor(t.DirectoryId).Should().Be(0);
	}

	[Fact]
	public async Task Ciclo_TenantComFederacaoDesabilitada_NaoEPercorrido()
	{
		var t = await TenantAsync(federationEnabled: false);
		factory.Graph.SetMembers(t.DirectoryId, t.GroupId, t.ObjectId);

		await Service().RunOnceAsync();

		factory.Graph.CallsFor(t.DirectoryId).Should().Be(0);
		(await RolesAsync(t)).Should().BeEmpty();
	}

	[Fact]
	public async Task Ciclo_TenantInativo_NaoEPercorrido()
	{
		var t = await TenantAsync();
		factory.Graph.SetMembers(t.DirectoryId, t.GroupId, t.ObjectId);

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			(await context.Tenants.FirstAsync(x => x.Id == t.TenantId)).Deactivate();
			await context.SaveChangesAsync();
		}

		await Service().RunOnceAsync();

		factory.Graph.CallsFor(t.DirectoryId).Should().Be(0);
	}

	[Fact]
	public async Task Ciclo_TenantDePlataforma_NuncaEElegivel()
	{
		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

			if (!await context.TenantFederations.AnyAsync(f => f.TenantId == SecureGatePlatform.TenantId))
			{
				context.TenantFederations.Add(new TenantFederation(SecureGatePlatform.TenantId, Guid.NewGuid()));
				await context.SaveChangesAsync();
			}
		}

		using var listScope = factory.Services.CreateScope();
		var eligible = await listScope.ServiceProvider.GetRequiredService<IDirectorySyncTenantSource>().ListEligibleAsync();

		eligible.Should().NotContain(e => e.TenantId == SecureGatePlatform.TenantId);
	}

	// ---- Lease ----

	[Fact]
	public async Task Lease_SegundaPassadaNoMesmoIntervalo_NaoRodaDeNovo()
	{
		var t = await TenantAsync();
		factory.Graph.SetMembers(t.DirectoryId, t.GroupId, t.ObjectId);

		(await Service().RunOnceAsync()).Ran.Should().BeTrue();
		var callsAfterFirst = factory.Graph.CallsFor(t.DirectoryId);

		(await Service().RunOnceAsync()).Ran.Should().BeFalse("o lease segue válido até a próxima execução devida");
		(await Service().RunOnceAsync()).Ran.Should().BeFalse("nem a mesma instância repete dentro do intervalo");
		factory.Graph.CallsFor(t.DirectoryId).Should().Be(callsAfterFirst);
	}

	[Fact]
	public async Task Lease_DuasInstanciasAoMesmoTempo_SoUmaRoda()
	{
		await TenantAsync();

		var runs = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() => Service().RunOnceAsync())));

		runs.Count(r => r.Ran).Should().Be(1);
	}

	[Fact]
	public async Task Lease_ValidoComOutraInstancia_NaoRoda()
	{
		await TakeLeaseAsync("outra-instancia", TimeSpan.FromMinutes(20));

		(await Service().RunOnceAsync()).Ran.Should().BeFalse();
	}

	[Fact]
	public async Task Lease_ExpiradoDeInstanciaQueCaiu_EAssumidoPorOutra()
	{
		await TakeLeaseAsync("instancia-que-caiu", TimeSpan.FromMinutes(-1));

		(await Service().RunOnceAsync()).Ran.Should().BeTrue();
	}

	[Fact]
	public async Task Lease_PassadoOIntervalo_RodaDeNovo()
	{
		await TenantAsync();
		(await Service().RunOnceAsync()).Ran.Should().BeTrue();

		var later = new ShiftedClock { Offset = TimeSpan.FromHours(25) };

		(await Service(later).RunOnceAsync()).Ran.Should().BeTrue("passadas 24 h o ciclo está devido");
	}

	[Fact]
	public async Task Lease_RespeitaOIntervaloConfigurado()
	{
		await TenantAsync();
		var options = new DirectorySyncOptions { IntervalHours = 2 };
		(await Service(options: options).RunOnceAsync()).Ran.Should().BeTrue();

		(await Service(new ShiftedClock { Offset = TimeSpan.FromHours(1) }, options: options).RunOnceAsync()).Ran.Should().BeFalse();
		(await Service(new ShiftedClock { Offset = TimeSpan.FromHours(3) }, options: options).RunOnceAsync()).Ran.Should().BeTrue();
	}

	[Fact]
	public async Task SemAppRegistrationDoEntra_OCicloNaoFazNadaNemTomaOLease()
	{
		var t = await TenantAsync();
		factory.Graph.SetMembers(t.DirectoryId, t.GroupId, t.ObjectId);

		(await Service(configured: false).RunOnceAsync()).Ran.Should().BeFalse();

		factory.Graph.CallsFor(t.DirectoryId).Should().Be(0);
		(await Service().RunOnceAsync()).Ran.Should().BeTrue("o lease não foi consumido pela passada que não rodou");
	}

	// ---- Apoio ----

	private async Task ResetLeaseAsync()
	{
		using var scope = factory.Services.CreatePlatformScope();
		await scope.ServiceProvider.GetRequiredService<SecureGateDbContext>().BackgroundLeases
			.Where(l => l.Name == DirectorySyncService.TaskName).ExecuteDeleteAsync();
	}

	private async Task TakeLeaseAsync(string owner, TimeSpan duration)
	{
		using var scope = factory.Services.CreateScope();
		(await scope.ServiceProvider.GetRequiredService<IBackgroundLeaseStore>()
			.TryAcquireAsync(DirectorySyncService.TaskName, owner, duration, DateTimeOffset.UtcNow))
			.Should().BeTrue();
	}

	/// <summary>Simula atribuição vinda da sincronização de um grupo cujo mapeamento já não existe.</summary>
	private async Task GrantDirectoryRoleAsync(Tenant1 t, Guid sourceGroup)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var role = await context.Roles.FirstAsync(r => r.TenantId == t.TenantId && r.NormalizedName == "FINANCEIRO");
		context.UserRoles.Add(new UserRole
		{
			UserId = t.UserId,
			RoleId = role.Id,
			Origin = RoleAssignmentOrigin.Directory,
			SourceGroupId = sourceGroup,
		});
		await context.SaveChangesAsync();
	}
}
