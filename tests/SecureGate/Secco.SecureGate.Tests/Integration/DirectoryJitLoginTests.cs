using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Api.Identity;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Provisionamento no primeiro login federado (JIT, ADR-0055, entrega B), provado SEM Entra real: o
/// <see cref="EntraSignInProcessor"/> recebe principals com os claims crus do Entra e um Graph falso. Sem conta pelo
/// login nem pelo e-mail, só o grupo de provisionamento cria a conta; todo o resto é a recusa genérica.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class DirectoryJitLoginTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private sealed record Scenario(Guid TenantId, Guid DirectoryId, Guid GroupId, Guid BaseRoleId);

	private static string Email(string prefix = "jit", string domain = "secco.test") => $"{prefix}-{Guid.NewGuid():N}@{domain}";

	/// <summary>Tenant com federação (domínio secco.test), perfil "base" mapeado ao grupo de provisionamento.</summary>
	private async Task<Scenario> ScenarioAsync(
		Guid? directoryId = null,
		bool provisioning = true,
		bool federationEnabled = true,
		bool tenantActive = true)
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantId, "base");
		var directory = directoryId ?? Guid.NewGuid();
		var groupId = Guid.NewGuid();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var federation = new TenantFederation(tenantId, directory);
		federation.SetEnabled(federationEnabled);
		federation.SetAllowedEmailDomains(["secco.test"]);
		context.TenantFederations.Add(federation);

		var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == "BASE");
		var mapping = new TenantGroupRoleMapping(tenantId, groupId, "Todos", role.Id);

		if (provisioning)
		{
			mapping.MarkAsProvisioningGroup();
		}

		context.TenantGroupRoleMappings.Add(mapping);

		if (!tenantActive)
		{
			(await context.Tenants.FirstAsync(t => t.Id == tenantId)).Deactivate();
		}

		await context.SaveChangesAsync();

		return new Scenario(tenantId, directory, groupId, role.Id);
	}

	private static ClaimsPrincipal Principal(Guid directoryId, Guid objectId, string? email)
	{
		var claims = new List<Claim>
		{
			new("tid", directoryId.ToString("D")),
			new("oid", objectId.ToString("D")),
		};

		if (email is not null)
		{
			claims.Add(new Claim("email", email));
		}

		return new ClaimsPrincipal(new ClaimsIdentity(claims, EntraSignInProcessor.LoginProvider));
	}

	private async Task<Secco.SharedKernel.Results.Result<User>> LoginAsync(Guid directoryId, Guid objectId, string? tokenEmail)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return await scope.ServiceProvider.GetRequiredService<EntraSignInProcessor>()
			.ProcessAsync(Principal(directoryId, objectId, tokenEmail));
	}

	private async Task<User?> FindByEmailAsync(string email)
	{
		using var scope = factory.Services.CreatePlatformScope();

		return await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByEmailAsync(email);
	}

	private async Task<IReadOnlyList<AuthorityAuditEntry>> TrailAsync(Guid tenantId)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.AuthorityAuditEntries.AsNoTracking()
			.Where(e => e.TenantId == tenantId && e.ActorType == "directory")
			.OrderBy(e => e.CreatedAt)
			.ToListAsync();
	}

	private int ChecksFor(Guid directoryId, Guid objectId) =>
		factory.Graph.Calls.Count(call => call == $"check:{directoryId:D}:{objectId:D}");

	// ---- Dentro do grupo ----

	[Fact]
	public async Task PessoaNoGrupoDeProvisionamento_SemConta_GanhaContaELoga_ComPerfisDoGrupo()
	{
		var s = await ScenarioAsync();
		var email = Email();
		var person = new DirectoryPerson(Guid.NewGuid(), "João Primeiro Login", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		var result = await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: email);

		result.IsSuccess.Should().BeTrue();
		var user = result.Value;
		user.TenantId.Should().Be(s.TenantId);
		user.PasswordHash.Should().BeNull();
		user.LocalLoginEnabled.Should().BeFalse();
		user.DirectoryManaged.Should().BeTrue();
		user.DisplayName.Should().Be("João Primeiro Login");

		using var scope = factory.Services.CreatePlatformScope();
		var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
		(await userManager.FindByLoginAsync(EntraSignInProcessor.LoginProvider, DirectoryLinkKey.Build(s.DirectoryId, person.ObjectId)))
			.Should().NotBeNull("o oid já nasce vinculado");

		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.UserRoles.AsNoTracking().AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == s.BaseRoleId))
			.Should().BeTrue("o login aplica os perfis mapeados logo depois de criar a conta");

		var trail = await TrailAsync(s.TenantId);
		trail.Should().Contain(e => e.Action == DirectorySyncActions.AccountCreated);
		JsonDocument.Parse(trail.First(e => e.Action == DirectorySyncActions.AccountCreated).Metadata!).RootElement
			.GetProperty("trigger").GetString().Should().Be("login");
	}

	[Fact]
	public async Task OEmailDaContaVemDoGraph_NuncaDoClaimDoToken()
	{
		var s = await ScenarioAsync();
		var graphEmail = Email("graph");
		var person = new DirectoryPerson(Guid.NewGuid(), "Pessoa", graphEmail, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		var result = await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: "claim-forjado@atacante.example");

		result.IsSuccess.Should().BeTrue();
		result.Value.Email.Should().Be(graphEmail);
		(await FindByEmailAsync("claim-forjado@atacante.example")).Should().BeNull();
	}

	[Fact]
	public async Task TokenSemClaimDeEmail_NoGrupo_AindaGanhaConta()
	{
		var s = await ScenarioAsync();
		var email = Email("semclaim");
		var person = new DirectoryPerson(Guid.NewGuid(), "Pessoa", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		(await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: null)).IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task EmVariosTenantsDoMesmoDiretorio_VenceOTenantCriadoPrimeiro()
	{
		var directory = Guid.NewGuid();
		var first = await ScenarioAsync(directory);
		var second = await ScenarioAsync(directory);
		var email = Email("dois");
		var person = new DirectoryPerson(Guid.NewGuid(), "Nos Dois Grupos", email, null);
		factory.Graph.SetPeople(directory, first.GroupId, person);
		factory.Graph.SetPeople(directory, second.GroupId, person);

		var result = await LoginAsync(directory, person.ObjectId, tokenEmail: null);

		result.IsSuccess.Should().BeTrue();
		result.Value.TenantId.Should().Be(first.TenantId, "ordem determinística: criação do tenant");
		(await TrailAsync(second.TenantId)).Should().BeEmpty();
	}

	// ---- Fora do grupo ----

	[Fact]
	public async Task PessoaForaDoGrupo_RecusaGenerica_SemCriarConta()
	{
		var s = await ScenarioAsync();
		var email = Email("fora");
		var person = new DirectoryPerson(Guid.NewGuid(), "Fora", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(Guid.NewGuid(), "Outro", Email(), null));
		factory.Graph.SetPerson(s.DirectoryId, person);

		var result = await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: email);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected, "a mesma recusa de sempre, sem revelar o motivo");
		(await FindByEmailAsync(email)).Should().BeNull();
	}

	[Fact]
	public async Task RecusaNegativa_FicaEmCacheCurtoPorOid_NaoAmplificaChamadasAoGraph()
	{
		var s = await ScenarioAsync();
		var person = new DirectoryPerson(Guid.NewGuid(), "Fora", Email("cache"), null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(Guid.NewGuid(), "Outro", Email(), null));

		(await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: null)).IsFailure.Should().BeTrue();
		var afterFirst = ChecksFor(s.DirectoryId, person.ObjectId);
		afterFirst.Should().Be(1);

		(await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: null)).IsFailure.Should().BeTrue();
		(await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: null)).IsFailure.Should().BeTrue();

		ChecksFor(s.DirectoryId, person.ObjectId).Should().Be(afterFirst, "a resposta negativa é cacheada por 5 minutos");
	}

	[Fact]
	public async Task OCacheNegativo_ECurto_EPorOid_OutraPessoaAindaConsulta()
	{
		EntraSignInProcessor.NegativeCacheDuration.Should().Be(TimeSpan.FromMinutes(5));

		var s = await ScenarioAsync();
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(Guid.NewGuid(), "Outro", Email(), null));
		var first = Guid.NewGuid();
		var second = Guid.NewGuid();

		await LoginAsync(s.DirectoryId, first, tokenEmail: null);
		await LoginAsync(s.DirectoryId, second, tokenEmail: null);

		ChecksFor(s.DirectoryId, first).Should().Be(1);
		ChecksFor(s.DirectoryId, second).Should().Be(1, "o cache é por oid, não por diretório");
	}

	[Fact]
	public async Task FalhaDoGraph_Recusa_MasNaoEntraNoCache_OProximoLoginTentaDeNovo()
	{
		var s = await ScenarioAsync();
		factory.Graph.SetFailure(s.DirectoryId, s.GroupId, SecureGateErrors.Federation.DirectoryUnavailable);
		var oid = Guid.NewGuid();

		(await LoginAsync(s.DirectoryId, oid, tokenEmail: null)).Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
		(await LoginAsync(s.DirectoryId, oid, tokenEmail: null)).IsFailure.Should().BeTrue();

		ChecksFor(s.DirectoryId, oid).Should().Be(2, "falha transitória não vira recusa de 5 minutos");
	}

	// ---- E-mail ----

	[Fact]
	public async Task DominioForaDaLista_Recusa_ERegistraNaTrilha()
	{
		var s = await ScenarioAsync();
		var email = Email("estrangeiro", "outra.com");
		var person = new DirectoryPerson(Guid.NewGuid(), "Estrangeiro", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		var result = await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: null);

		result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
		(await FindByEmailAsync(email)).Should().BeNull();
		var entry = (await TrailAsync(s.TenantId)).Should().ContainSingle().Subject;
		entry.Action.Should().Be(DirectorySyncActions.EmailRefused);
		var metadata = JsonDocument.Parse(entry.Metadata!).RootElement;
		metadata.GetProperty("trigger").GetString().Should().Be("login");
		metadata.GetProperty("reason").GetString().Should().Be("domain-not-allowed");
		entry.Metadata.Should().NotContain("outra.com");
	}

	[Fact]
	public async Task EmailJaExistenteEmOutroTenant_Recusa_SemCriarSegundaConta()
	{
		var s = await ScenarioAsync();
		var other = await IdentitySeed.TenantAsync(factory);
		var email = Email("existente");
		var existing = await IdentitySeed.UserAsync(factory, other, email);
		var person = new DirectoryPerson(Guid.NewGuid(), "Duplicada", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		// O claim do token traz outro e-mail: a conta existente não é achada por ele, e o JIT bate na colisão
		var result = await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: Email("outro"));

		result.IsFailure.Should().BeTrue();
		(await FindByEmailAsync(email))!.Id.Should().Be(existing);
		var entry = (await TrailAsync(s.TenantId)).Should().ContainSingle().Subject;
		JsonDocument.Parse(entry.Metadata!).RootElement.GetProperty("reason").GetString().Should().Be("email-in-use");
	}

	[Fact]
	public async Task SemConsentimentoDePerfil_Recusa_SemCriar_ENaoCacheia()
	{
		var s = await ScenarioAsync();
		var email = Email("semconsent");
		var person = new DirectoryPerson(Guid.NewGuid(), "Pessoa", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);
		factory.Graph.ForbidProfiles(s.DirectoryId);

		(await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: null)).IsFailure.Should().BeTrue();
		(await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: null)).IsFailure.Should().BeTrue();

		(await FindByEmailAsync(email)).Should().BeNull();
		ChecksFor(s.DirectoryId, person.ObjectId).Should().Be(2);
	}

	// ---- Escopo do provisionamento ----

	[Theory]
	[InlineData("desmarcado")]
	[InlineData("federacao-desabilitada")]
	[InlineData("tenant-inativo")]
	public async Task TenantSemProvisionamentoValido_NaoConsultaOGraphENaoCria(string cenario)
	{
		var s = await ScenarioAsync(
			provisioning: cenario != "desmarcado",
			federationEnabled: cenario != "federacao-desabilitada",
			tenantActive: cenario != "tenant-inativo");
		var email = Email(cenario);
		var person = new DirectoryPerson(Guid.NewGuid(), "Pessoa", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		var result = await LoginAsync(s.DirectoryId, person.ObjectId, tokenEmail: null);

		result.IsFailure.Should().BeTrue();
		(await FindByEmailAsync(email)).Should().BeNull();
		ChecksFor(s.DirectoryId, person.ObjectId).Should().Be(0);
	}

	// Defesa em profundidade: mesmo que federação e grupo de provisionamento existam no tenant de plataforma (o
	// cadastro normal não chega lá), o diretório do cliente nunca cria conta onde moram os operadores
	[Fact]
	public async Task TenantDePlataforma_NuncaEAlvoDoProvisionamento_MesmoComGrupoMarcado()
	{
		var directoryId = Guid.NewGuid();
		var groupId = Guid.NewGuid();
		var roleName = $"base-{Guid.NewGuid():N}";
		await IdentitySeed.RoleAsync(factory, SecureGatePlatform.TenantId, roleName);

		// Outro teste pode já ter plantado a federação do tenant de plataforma (é 1:1 com o tenant): reaproveita e
		// devolve o estado anterior no fim
		bool existed;
		Guid previousDirectory = Guid.Empty;
		bool previousEnabled = false;
		IReadOnlyList<string> previousDomains = [];

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			var federation = await context.TenantFederations.FirstOrDefaultAsync(f => f.TenantId == SecureGatePlatform.TenantId);
			existed = federation is not null;

			if (federation is null)
			{
				federation = new TenantFederation(SecureGatePlatform.TenantId, directoryId);
				context.TenantFederations.Add(federation);
			}
			else
			{
				previousDirectory = federation.DirectoryId;
				previousEnabled = federation.IsEnabled;
				previousDomains = federation.GetAllowedEmailDomains();
				federation.UpdateDirectory(directoryId);
			}

			federation.SetEnabled(true);
			federation.SetAllowedEmailDomains(["secco.test"]);

			var role = await context.Roles.FirstAsync(r => r.TenantId == SecureGatePlatform.TenantId && r.Name == roleName);
			var mapping = new TenantGroupRoleMapping(SecureGatePlatform.TenantId, groupId, "Todos", role.Id);
			mapping.MarkAsProvisioningGroup();
			context.TenantGroupRoleMappings.Add(mapping);
			await context.SaveChangesAsync();
		}

		try
		{
			var email = Email("plataforma");
			var person = new DirectoryPerson(Guid.NewGuid(), "Pessoa", email, null);
			factory.Graph.SetPeople(directoryId, groupId, person);

			var result = await LoginAsync(directoryId, person.ObjectId, tokenEmail: null);

			result.IsFailure.Should().BeTrue();
			(await FindByEmailAsync(email)).Should().BeNull();
			ChecksFor(directoryId, person.ObjectId).Should().Be(0);
		}
		finally
		{
			using var scope = factory.Services.CreatePlatformScope();
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			context.TenantGroupRoleMappings.RemoveRange(
				context.TenantGroupRoleMappings.Where(m => m.TenantId == SecureGatePlatform.TenantId && m.EntraGroupId == groupId));
			var federation = await context.TenantFederations.FirstAsync(f => f.TenantId == SecureGatePlatform.TenantId);

			if (existed)
			{
				federation.UpdateDirectory(previousDirectory);
				federation.SetEnabled(previousEnabled);
				federation.SetAllowedEmailDomains(previousDomains);
			}
			else
			{
				context.TenantFederations.Remove(federation);
			}

			await context.SaveChangesAsync();
		}
	}

	[Fact]
	public async Task OutroDiretorio_NuncaProvisionaNoTenant()
	{
		var s = await ScenarioAsync();
		var email = Email("outrotid");
		var person = new DirectoryPerson(Guid.NewGuid(), "Pessoa", email, null);
		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, person);

		var result = await LoginAsync(Guid.NewGuid(), person.ObjectId, tokenEmail: null);

		result.IsFailure.Should().BeTrue("o pin de diretório vale: o tid do token tem de ser o registrado");
		(await FindByEmailAsync(email)).Should().BeNull();
	}

	// ---- Conta desativada pelo diretório ----

	[Fact]
	public async Task ContaDesativadaPeloDiretorio_ReentraNoGrupo_OLoginNaoReativa_SoOCiclo()
	{
		var s = await ScenarioAsync();
		var email = Email("desativada");
		var userId = await IdentitySeed.UserAsync(factory, s.TenantId, email);
		var objectId = Guid.NewGuid();

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
			var user = (await userManager.FindByIdAsync(userId.ToString()))!;
			(await userManager.AddLoginAsync(user, new UserLoginInfo(
				DirectoryLinkKey.Provider, DirectoryLinkKey.Build(s.DirectoryId, objectId), "Entra"))).Succeeded.Should().BeTrue();
			user.DirectoryManaged = true;
			user.DeactivationOrigin = DeactivationOrigin.Directory;
			await userManager.UpdateAsync(user);
			await userManager.SetLockoutEnabledAsync(user, true);
			await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
		}

		factory.Graph.SetPeople(s.DirectoryId, s.GroupId, new DirectoryPerson(objectId, "Voltou", email, null));

		var result = await LoginAsync(s.DirectoryId, objectId, tokenEmail: email);

		result.Error.Should().Be(SecureGateErrors.Federation.SignInRejected);
		using var verify = factory.Services.CreatePlatformScope();
		var after = (await verify.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString()))!;
		UserStatuses.From(after.LockoutEnabled, after.LockoutEnd, DateTimeOffset.UtcNow).Should().Be(UserStatuses.Deactivated);
		after.DeactivationOrigin.Should().Be(DeactivationOrigin.Directory);
	}
}
