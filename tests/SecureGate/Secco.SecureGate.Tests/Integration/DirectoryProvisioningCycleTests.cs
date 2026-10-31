using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Federation;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Provisionamento de contas pelo diretório no ciclo (ADR-0055, entrega B), contra o banco real (credencial de runtime,
/// sem posse, com o RLS valendo) e um Graph falso: criar, adotar, desativar e reativar, o freio de massa, o
/// fail-closed e a trilha. O serviço é conduzido por <c>RunOnceAsync</c>, como o ciclo da entrega A.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class DirectoryProvisioningCycleTests(SecureGateApiFactory factory) : IAsyncLifetime
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

	private DirectorySyncService Service(DirectorySyncOptions? options = null) => new(
		factory.Services.GetRequiredService<IServiceScopeFactory>(),
		Options.Create(options ?? new DirectorySyncOptions()),
		Options.Create(new SecureGateEntraIdOptions { ClientId = "client-id", ClientSecret = "client-secret" }),
		TimeProvider.System,
		NullLogger<DirectorySyncService>.Instance);

	private async Task CycleAsync()
	{
		await ResetLeaseAsync();
		(await Service().RunOnceAsync()).Ran.Should().BeTrue();
	}

	private async Task ResetLeaseAsync()
	{
		using var scope = factory.Services.CreatePlatformScope();
		await scope.ServiceProvider.GetRequiredService<SecureGateDbContext>().BackgroundLeases
			.Where(l => l.Name == DirectorySyncService.TaskName).ExecuteDeleteAsync();
	}

	private sealed record Scenario(Guid TenantId, Guid DirectoryId, Guid GroupId, Guid MappingId, Guid BaseRoleId);

	private static string Email(string prefix = "dir", string domain = "secco.test") => $"{prefix}-{Guid.NewGuid():N}@{domain}";

	private static DirectoryPerson Person(string email, string? name = "Pessoa do Diretório") =>
		new(Guid.NewGuid(), name, email, null);

	/// <summary>Tenant com federação habilitada (domínio secco.test), perfil "base" e o grupo mapeado a ele.</summary>
	private async Task<Scenario> ScenarioAsync(bool provisioning = true, params string[] domains)
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantId, "base");
		var directoryId = Guid.NewGuid();
		var groupId = Guid.NewGuid();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var federation = new TenantFederation(tenantId, directoryId);
		federation.SetAllowedEmailDomains(domains.Length == 0 ? ["secco.test"] : domains);
		context.TenantFederations.Add(federation);

		var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == "BASE");
		var mapping = new TenantGroupRoleMapping(tenantId, groupId, "Todos os colaboradores", role.Id);

		if (provisioning)
		{
			mapping.MarkAsProvisioningGroup();
		}

		context.TenantGroupRoleMappings.Add(mapping);
		await context.SaveChangesAsync();

		return new Scenario(tenantId, directoryId, groupId, mapping.Id, role.Id);
	}

	/// <summary>Conta do tenant já vinculada ao diretório por <c>{tid}:{oid}</c>.</summary>
	private async Task<(Guid UserId, Guid ObjectId, string Email)> LinkedAccountAsync(
		Scenario s,
		bool managed = false,
		bool deactivated = false,
		DeactivationOrigin origin = DeactivationOrigin.None,
		params string[] roles)
	{
		var email = Email("conta");
		var userId = await IdentitySeed.UserAsync(factory, s.TenantId, email, roles);
		var objectId = Guid.NewGuid();

		using var scope = factory.Services.CreatePlatformScope();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
		var user = (await userManager.FindByIdAsync(userId.ToString()))!;
		(await userManager.AddLoginAsync(user, new UserLoginInfo(
			DirectoryLinkKey.Provider, DirectoryLinkKey.Build(s.DirectoryId, objectId), "Entra"))).Succeeded.Should().BeTrue();

		user.DirectoryManaged = managed;
		user.DeactivationOrigin = origin;
		(await userManager.UpdateAsync(user)).Succeeded.Should().BeTrue();

		if (deactivated)
		{
			(await userManager.SetLockoutEnabledAsync(user, true)).Succeeded.Should().BeTrue();
			(await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue)).Succeeded.Should().BeTrue();
		}

		return (userId, objectId, email);
	}

	private async Task<User?> FindUserAsync(string email)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email);
	}

	private async Task<User> UserAsync(Guid userId)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return (await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString()))!;
	}

	private async Task<IReadOnlyList<AuthorityAuditEntry>> TrailAsync(Scenario s)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.AuthorityAuditEntries.AsNoTracking()
			.Where(e => e.TenantId == s.TenantId && e.ActorType == "directory")
			.OrderBy(e => e.CreatedAt)
			.ToListAsync();
	}

	private static string[] Actions(IEnumerable<AuthorityAuditEntry> trail) =>
		[.. trail.Select(entry => entry.Action["sincronizacao-do-diretorio.".Length..])];

	// ---- Criação ----

	[Fact]
	public async Task Ciclo_PessoaDoGrupoSemConta_GanhaContaDonaSemSenha_GeridaEVinculada()
	{
		var s = await ScenarioAsync();
		var email = Email("nova");
		var person = Person(email, "Maria da Silva");
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		await CycleAsync();

		var user = (await FindUserAsync(email))!;
		user.Should().NotBeNull();
		user.TenantId.Should().Be(s.TenantId);
		user.PasswordHash.Should().BeNull("a credencial é só o login federado");
		user.LocalLoginEnabled.Should().BeFalse();
		user.EmailConfirmed.Should().BeTrue();
		user.DirectoryManaged.Should().BeTrue();
		user.DeactivationOrigin.Should().Be(DeactivationOrigin.None);
		user.DisplayName.Should().Be("Maria da Silva");

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.UserLogins.AsNoTracking().SingleAsync(l => l.UserId == user.Id))
			.Should().BeEquivalentTo(new
			{
				LoginProvider = DirectoryLinkKey.Provider,
				ProviderKey = DirectoryLinkKey.Build(s.DirectoryId, person.ObjectId),
			});
		(await context.TenantMemberships.AsNoTracking().SingleAsync(m => m.UserId == user.Id))
			.Should().BeEquivalentTo(new { s.TenantId, IsActive = true }, "o vínculo de dono nasce com a conta (ADR-0040)");

		var roles = await context.UserRoles.AsNoTracking().Where(ur => ur.UserId == user.Id).ToListAsync();
		roles.Should().ContainSingle().Which.Should().BeEquivalentTo(
			new { RoleId = s.BaseRoleId, Origin = RoleAssignmentOrigin.Directory, SourceGroupId = (Guid?)s.GroupId });

		factory.Emails.For(email).Should().BeEmpty("sem convite e sem e-mail algum");
	}

	[Fact]
	public async Task Ciclo_ContaCriada_TrilhaDiretorioComOidENuncaOEmail()
	{
		var s = await ScenarioAsync();
		var email = Email("trilha");
		var person = Person(email);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		await CycleAsync();

		var user = (await FindUserAsync(email))!;
		var trail = await TrailAsync(s);
		var entry = trail.Single(e => e.Action == DirectorySyncActions.AccountCreated);
		entry.ActorType.Should().Be("directory");
		entry.ActorId.Should().Be(s.DirectoryId.ToString("D"));
		entry.ResourceType.Should().Be("user");
		entry.ResourceId.Should().Be(user.Id.ToString("D"));
		var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
		metadata.GetProperty("category").GetString().Should().Be("accounts");
		metadata.GetProperty("trigger").GetString().Should().Be("cycle");
		metadata.GetProperty("objectId").GetGuid().Should().Be(person.ObjectId);
		trail.Should().OnlyContain(e => !(e.Metadata ?? string.Empty).Contains(email, StringComparison.OrdinalIgnoreCase));
		Actions(trail).Should().Equal("conta-criada", "perfil-acrescentado");
		entry.CreatedAt.Should().BeOnOrBefore(trail.Single(e => e.Action == DirectorySyncActions.RoleAdded).CreatedAt);
	}

	[Fact]
	public async Task Ciclo_DominioForaDaLista_NaoCriaConta_ERegistraSemOEmail()
	{
		var s = await ScenarioAsync();
		var email = Email("fora", "outra.com");
		var person = Person(email);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		await CycleAsync();

		(await FindUserAsync(email)).Should().BeNull();
		var trail = await TrailAsync(s);
		var entry = trail.Should().ContainSingle().Subject;
		entry.Action.Should().Be(DirectorySyncActions.EmailRefused);
		var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
		metadata.GetProperty("reason").GetString().Should().Be("domain-not-allowed");
		metadata.GetProperty("objectId").GetGuid().Should().Be(person.ObjectId);
		entry.Metadata.Should().NotContain("outra.com");
	}

	[Fact]
	public async Task Ciclo_FederacaoSemDominiosPermitidos_NaoCriaNinguem()
	{
		var s = await ScenarioAsync();

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			(await context.TenantFederations.SingleAsync(f => f.TenantId == s.TenantId)).SetAllowedEmailDomains([]);
			await context.SaveChangesAsync();
		}

		var email = Email("semlista");
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(email));

		await CycleAsync();

		(await FindUserAsync(email)).Should().BeNull();
	}

	[Fact]
	public async Task Ciclo_EmailJaPertenceAContaDeOutroTenant_NaoCriaSegundaConta_ERegistraAColisao()
	{
		var s = await ScenarioAsync();
		var other = await IdentitySeed.TenantAsync(factory);
		var email = Email("colisao");
		var existing = await IdentitySeed.UserAsync(factory, other, email);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(email));

		await CycleAsync();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var normalized = email.ToUpperInvariant();
		(await context.Users.AsNoTracking().Where(u => u.NormalizedEmail == normalized).Select(u => u.Id).ToListAsync())
			.Should().Equal([existing], "o e-mail é único na instalação");
		var entry = (await TrailAsync(s)).Should().ContainSingle().Subject;
		entry.Action.Should().Be(DirectorySyncActions.EmailRefused);
		JsonDocument.Parse(entry.Metadata!).RootElement.GetProperty("reason").GetString().Should().Be("email-in-use");
	}

	[Fact]
	public async Task Ciclo_Repetido_E_Idempotente_NaoCriaDuasVezesNemRepeteATrilha()
	{
		var s = await ScenarioAsync();
		var email = Email("idem");
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(email));

		await CycleAsync();
		var userId = (await FindUserAsync(email))!.Id;
		var trailAfterFirst = Actions(await TrailAsync(s));

		await CycleAsync();

		(await FindUserAsync(email))!.Id.Should().Be(userId);
		Actions(await TrailAsync(s)).Should().Equal(trailAfterFirst, "nada a mudar, nada a registrar");
	}

	[Fact]
	public async Task Ciclo_GrupoNaoMarcado_NaoCriaConta()
	{
		var s = await ScenarioAsync(provisioning: false);
		var email = Email("desmarcado");
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(email));

		await CycleAsync();

		(await FindUserAsync(email)).Should().BeNull("sem o ato explícito do operador o diretório não decide quem tem conta");
		factory.Graph.Calls.Should().NotContain(call => call.StartsWith($"profiles:{s.DirectoryId:D}", StringComparison.Ordinal));
	}

	// ---- Adoção ----

	[Fact]
	public async Task Ciclo_ContaVinculadaPorOidNoGrupo_EAdotada_ComTrilha()
	{
		var s = await ScenarioAsync();
		var account = await LinkedAccountAsync(s);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(account.ObjectId, "Antiga", account.Email, null));

		await CycleAsync();

		(await UserAsync(account.UserId)).DirectoryManaged.Should().BeTrue();
		Actions(await TrailAsync(s)).Should().Equal("conta-adotada", "perfil-acrescentado");
	}

	[Fact]
	public async Task Ciclo_ContaSemVinculoComODiretorio_NuncaEAdotadaNemCriadaEmDuplicidade()
	{
		var s = await ScenarioAsync();
		var email = Email("semvinculo");
		var userId = await IdentitySeed.UserAsync(factory, s.TenantId, email);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(email));

		await CycleAsync();

		(await UserAsync(userId)).DirectoryManaged.Should().BeFalse("a adoção exige o vínculo por oid, não o e-mail");
		var entry = (await TrailAsync(s)).Should().ContainSingle().Subject;
		JsonDocument.Parse(entry.Metadata!).RootElement.GetProperty("reason").GetString().Should().Be("email-in-use");
	}

	[Fact]
	public async Task Ciclo_ContaAdministrativaNoGrupo_NuncaEAdotada()
	{
		var s = await ScenarioAsync();
		var account = await LinkedAccountAsync(s);

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			context.TenantAdminGrants.Add(new TenantAdminGrant(
				account.UserId, await IdentitySeed.TenantAsync(factory), "operador", DateTimeOffset.UtcNow));
			await context.SaveChangesAsync();
		}

		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(account.ObjectId, "Admin", account.Email, null));

		await CycleAsync();

		(await UserAsync(account.UserId)).DirectoryManaged.Should().BeFalse();
		Actions(await TrailAsync(s)).Should().NotContain("conta-adotada");
	}

	// ---- Desativação ----

	[Fact]
	public async Task Ciclo_ContaGeridaForaDoGrupo_EDesativadaComOrigemDiretorio_ERevogaAsSessoes()
	{
		var s = await ScenarioAsync();
		var gone = await LinkedAccountAsync(s, managed: true);
		var stampBefore = (await UserAsync(gone.UserId)).SecurityStamp;
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(Email("fica")));

		await CycleAsync();

		var user = await UserAsync(gone.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Deactivated);
		user.DeactivationOrigin.Should().Be(DeactivationOrigin.Directory);
		user.DirectoryManaged.Should().BeTrue("a conta não é excluída");
		user.SecurityStamp.Should().NotBe(stampBefore, "o stamp muda: sessões e refresh tokens caem (ADR-0032)");
		Actions(await TrailAsync(s)).Should().Contain("conta-desativada");
	}

	[Fact]
	public async Task Ciclo_ContaSoLocal_ForaDoGrupo_NuncaETocada()
	{
		var s = await ScenarioAsync();
		var local = await LinkedAccountAsync(s, managed: false);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(Email("fica")));

		await CycleAsync();

		var user = await UserAsync(local.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		user.DeactivationOrigin.Should().Be(DeactivationOrigin.None);
	}

	[Fact]
	public async Task Ciclo_ContaGeridaQueERaDeOutroTenant_NaoETocada()
	{
		// Isolamento: o ciclo de um tenant só alcança as contas DONAS dele
		var s = await ScenarioAsync();
		var other = await ScenarioAsync();
		var otherAccount = await LinkedAccountAsync(other, managed: true);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(Email("fica")));
		factory.Graph.SetPeople(other.DirectoryId, other.GroupId, new DirectoryPerson(otherAccount.ObjectId, "No grupo", otherAccount.Email, null));

		await CycleAsync();

		var user = await UserAsync(otherAccount.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
	}

	[Fact]
	public async Task Ciclo_UltimoMembroAtivoDeUmPerfil_NaoEDesativado_ERegistraABarragem()
	{
		var s = await ScenarioAsync();
		await IdentitySeed.RoleAsync(factory, s.TenantId, "diretoria");
		var sole = await LinkedAccountAsync(s, managed: true, roles: "diretoria");
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(Email("fica")));

		await CycleAsync();

		var user = await UserAsync(sole.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		var entry = (await TrailAsync(s)).Single(e => e.Action == DirectorySyncActions.DeactivationBlocked);
		var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
		metadata.GetProperty("reason").GetString().Should().Be("last-active-member");
		metadata.GetProperty("role").GetString().Should().Be("diretoria");
	}

	[Fact]
	public async Task Ciclo_ContaGeridaAdministrativa_NaoEDesativada()
	{
		var s = await ScenarioAsync();
		var admin = await LinkedAccountAsync(s, managed: true);

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			context.TenantAdminGrants.Add(new TenantAdminGrant(
				admin.UserId, await IdentitySeed.TenantAsync(factory), "operador", DateTimeOffset.UtcNow));
			await context.SaveChangesAsync();
		}

		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, Person(Email("fica")));

		await CycleAsync();

		var user = await UserAsync(admin.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		var entry = (await TrailAsync(s)).Single(e => e.Action == DirectorySyncActions.DeactivationBlocked);
		JsonDocument.Parse(entry.Metadata!).RootElement.GetProperty("reason").GetString().Should().Be("administrative-account");
	}

	// ---- Reativação ----

	[Fact]
	public async Task Ciclo_ContaDesativadaPeloDiretorio_DeVoltaAoGrupo_EReativada()
	{
		var s = await ScenarioAsync();
		var back = await LinkedAccountAsync(s, managed: true, deactivated: true, origin: DeactivationOrigin.Directory);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(back.ObjectId, "Voltou", back.Email, null));

		await CycleAsync();

		var user = await UserAsync(back.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		user.DeactivationOrigin.Should().Be(DeactivationOrigin.None);
		Actions(await TrailAsync(s)).Should().Contain("conta-reativada");
	}

	[Theory]
	[InlineData(DeactivationOrigin.Manual)]
	[InlineData(DeactivationOrigin.None)]
	public async Task Ciclo_ContaDesativadaPorOutraOrigem_DeVoltaAoGrupo_NuncaEReativada(DeactivationOrigin origin)
	{
		var s = await ScenarioAsync();
		var blocked = await LinkedAccountAsync(s, managed: true, deactivated: true, origin: origin);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(blocked.ObjectId, "No grupo", blocked.Email, null));

		await CycleAsync();

		var user = await UserAsync(blocked.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Deactivated);
		user.DeactivationOrigin.Should().Be(origin);
		Actions(await TrailAsync(s)).Should().NotContain("conta-reativada");
	}

	// ---- O armazenamento sozinho (defesa em profundidade: o plano e o banco guardam as mesmas regras) ----

	private async Task<TResult> WithStoreAsync<TResult>(Func<IDirectoryAccountStore, Task<TResult>> action)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return await action(scope.ServiceProvider.GetRequiredService<IDirectoryAccountStore>());
	}

	[Fact]
	public async Task Store_Reativar_NuncaDesfazDesativacaoManual_MesmoQuandoPedido()
	{
		var s = await ScenarioAsync();
		var manual = await LinkedAccountAsync(s, managed: true, deactivated: true, origin: DeactivationOrigin.Manual);

		(await WithStoreAsync(store => store.ReactivateAsync(s.TenantId, manual.UserId))).Should().BeFalse();

		var user = await UserAsync(manual.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Deactivated);
		user.DeactivationOrigin.Should().Be(DeactivationOrigin.Manual);
	}

	// O plano é calculado antes da aplicação: um admin que desativa a conta nesse meio-tempo não pode ter a origem
	// trocada para Directory, senão o ciclo seguinte desfaria a decisão dele
	[Fact]
	public async Task Store_Desativar_ContaJaDesativadaAMao_NaoTrocaAOrigemParaDiretorio()
	{
		var s = await ScenarioAsync();
		var manual = await LinkedAccountAsync(s, managed: true, deactivated: true, origin: DeactivationOrigin.Manual);

		(await WithStoreAsync(store => store.DeactivateAsync(s.TenantId, manual.UserId))).Should().BeFalse();

		(await UserAsync(manual.UserId)).DeactivationOrigin.Should().Be(DeactivationOrigin.Manual);
	}

	[Fact]
	public async Task Store_Desativar_ContaNaoGerida_NuncaEDesativada()
	{
		var s = await ScenarioAsync();
		var unmanaged = await LinkedAccountAsync(s, managed: false);

		(await WithStoreAsync(store => store.DeactivateAsync(s.TenantId, unmanaged.UserId))).Should().BeFalse();

		var user = await UserAsync(unmanaged.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
	}

	[Fact]
	public async Task Store_Desativar_GravaOrigemDiretorio_ELockoutSemFim_EReativarOLimpa()
	{
		var s = await ScenarioAsync();
		var account = await LinkedAccountAsync(s, managed: true);

		(await WithStoreAsync(store => store.DeactivateAsync(s.TenantId, account.UserId))).Should().BeTrue();

		var deactivated = await UserAsync(account.UserId);
		UserStatuses.From(deactivated.LockoutEnabled, deactivated.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Deactivated);
		deactivated.DeactivationOrigin.Should().Be(DeactivationOrigin.Directory);

		(await WithStoreAsync(store => store.ReactivateAsync(s.TenantId, account.UserId))).Should().BeTrue();

		var reactivated = await UserAsync(account.UserId);
		UserStatuses.From(reactivated.LockoutEnabled, reactivated.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		reactivated.DeactivationOrigin.Should().Be(DeactivationOrigin.None);
	}

	[Fact]
	public async Task Store_ContaDeOutroTenant_NaoSeAdotaNemSeDesativaNemSeReativa()
	{
		var s = await ScenarioAsync();
		var foreign = await LinkedAccountAsync(await ScenarioAsync(), managed: true, deactivated: true, origin: DeactivationOrigin.Directory);

		(await WithStoreAsync(store => store.AdoptAsync(s.TenantId, foreign.UserId))).Should().BeFalse();
		(await WithStoreAsync(store => store.DeactivateAsync(s.TenantId, foreign.UserId))).Should().BeFalse();
		(await WithStoreAsync(store => store.ReactivateAsync(s.TenantId, foreign.UserId))).Should().BeFalse();

		(await UserAsync(foreign.UserId)).DeactivationOrigin.Should().Be(DeactivationOrigin.Directory, "nada mudou");
	}

	[Fact]
	public async Task Store_Criar_EmailQueJaExiste_NaoCriaESemDeixarRastroDeConta()
	{
		var s = await ScenarioAsync();
		var other = await IdentitySeed.TenantAsync(factory);
		var email = Email("duplicado");
		await IdentitySeed.UserAsync(factory, other, email);

		var outcome = await WithStoreAsync(store => store.CreateAsync(
			s.TenantId, s.DirectoryId, Guid.CreateVersion7(), Guid.NewGuid(), new DirectoryAccountDraft(email, null)));

		outcome.Should().Be(CreateDirectoryAccountOutcome.EmailInUse);

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var normalizedEmail = email.ToUpperInvariant();
		(await context.Users.AsNoTracking().CountAsync(u => u.NormalizedEmail == normalizedEmail)).Should().Be(1);
		(await context.TenantMemberships.AsNoTracking().CountAsync(m => m.TenantId == s.TenantId)).Should().Be(0);
	}

	// ---- Freio de massa ----

	[Fact]
	public async Task Ciclo_DesativacoesAcimaDoFreio_AbortaOTenant_NemCriaNemDesativaNemMexeEmPerfil()
	{
		var s = await ScenarioAsync();
		var staying = new List<(Guid UserId, Guid ObjectId, string Email)>();

		for (var i = 0; i < 3; i++)
		{
			staying.Add(await LinkedAccountAsync(s, managed: true));
		}

		var leaving = new List<(Guid UserId, Guid ObjectId, string Email)>();

		for (var i = 0; i < 7; i++)
		{
			leaving.Add(await LinkedAccountAsync(s, managed: true));
		}

		var newcomerEmail = Email("nova");
		factory.Graph.SetPeople(
			s.DirectoryId,
			s.GroupId,
			[.. staying.Select(a => new DirectoryPerson(a.ObjectId, "Fica", a.Email, null)), Person(newcomerEmail)]);

		await CycleAsync();

		(await FindUserAsync(newcomerEmail)).Should().BeNull("o freio aborta o tenant inteiro: nem cria");

		foreach (var account in leaving)
		{
			var user = await UserAsync(account.UserId);
			UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		}

		var trail = await TrailAsync(s);
		Actions(trail).Should().Equal(["ciclo-abortado"], "nem mexe em perfil nem em conta");
		var metadata = JsonDocument.Parse(trail.Single().Metadata!).RootElement;
		metadata.GetProperty("category").GetString().Should().Be("accounts");
		metadata.GetProperty("count").GetInt32().Should().Be(7);
		metadata.GetProperty("total").GetInt32().Should().Be(10);
	}

	[Fact]
	public async Task Ciclo_GrupoVazioNoGraph_NaoDerrubaATodosDeUmaVez_QuandoHaMuitasContas()
	{
		// A Microsoft devolveu uma lista vazia (ou truncada): o freio impede que a empresa inteira seja desativada
		var s = await ScenarioAsync();
		var accounts = new List<(Guid UserId, Guid ObjectId, string Email)>();

		for (var i = 0; i < 6; i++)
		{
			accounts.Add(await LinkedAccountAsync(s, managed: true));
		}

		factory.Graph.SetPeople(s.DirectoryId, s.GroupId);

		await CycleAsync();

		foreach (var account in accounts)
		{
			var user = await UserAsync(account.UserId);
			UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		}
	}

	// ---- Fail-closed ----

	[Fact]
	public async Task Ciclo_LeituraDoGrupoQueFalha_NaoMudaNadaNoTenant_ERegistra()
	{
		var s = await ScenarioAsync();
		var managed = await LinkedAccountAsync(s, managed: true);
		factory.Graph.SetFailure(s.DirectoryId, s.GroupId, SecureGateErrors.Federation.DirectoryUnavailable);

		await CycleAsync();

		var user = await UserAsync(managed.UserId);
		UserStatuses.From(user.LockoutEnabled, user.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		Actions(await TrailAsync(s)).Should().Equal("leitura-falhou");
	}

	[Fact]
	public async Task Ciclo_SemConsentimentoDeLeituraDePerfil_ProvisionamentoRecusa_EPerfisSeguem()
	{
		var s = await ScenarioAsync();
		var member = await LinkedAccountAsync(s);
		var managedOut = await LinkedAccountAsync(s, managed: true);
		var email = Email("naocria");
		factory.Graph.SetPeople(
			s.DirectoryId, s.GroupId, new DirectoryPerson(member.ObjectId, "Membro", member.Email, null), Person(email));
		factory.Graph.ForbidProfiles(s.DirectoryId);

		await CycleAsync();

		(await FindUserAsync(email)).Should().BeNull("sem User.ReadBasic.All o provisionamento recusa");
		var gone = await UserAsync(managedOut.UserId);
		UserStatuses.From(gone.LockoutEnabled, gone.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Active);
		(await UserAsync(member.UserId)).DirectoryManaged.Should().BeFalse();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.UserRoles.AsNoTracking().AnyAsync(ur => ur.UserId == member.UserId && ur.RoleId == s.BaseRoleId))
			.Should().BeTrue("a reconciliação de perfis da entrega A segue funcionando");

		var trail = await TrailAsync(s);
		var failed = trail.Single(e => e.Action == DirectorySyncActions.ReadFailed);
		var metadata = JsonDocument.Parse(failed.Metadata!).RootElement;
		metadata.GetProperty("error").GetString().Should().Be(SecureGateErrors.Federation.ConsentRequired.Code);
		metadata.GetProperty("category").GetString().Should().Be("accounts");
	}

	[Fact]
	public async Task Ciclo_FalhaNumTenant_NaoImpedeOProvisionamentoDosOutros()
	{
		var broken = await ScenarioAsync();
		var healthy = await ScenarioAsync();
		factory.Graph.ThrowFor(broken.DirectoryId);
		var email = Email("saudavel");
		factory.Graph.SetPeople(healthy.DirectoryId, healthy.GroupId, Person(email));

		await ResetLeaseAsync();
		var run = await Service().RunOnceAsync();

		run.Failed.Should().BeGreaterThanOrEqualTo(1);
		(await FindUserAsync(email)).Should().NotBeNull();
	}
}
