using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Domain.Elevation;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Isolamento de tenant no banco do SecureGate (ADR-0041, rodada 1): os filtros globais nomeados
/// "Tenant" seguram SOZINHOS, sem filtro manual e sem escopo = nada aparece (fail-closed). Os dados
/// são criados em modo plataforma e lidos dentro de cada escopo, por contextos que NÃO passam pelos
/// repositórios (que ainda carregam filtros manuais redundantes).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class TenantIsolationTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private const string Product = "isolamento";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Seed _s = null!;

	/// <summary>Ids de tudo o que o teste criou nos tenants A e B.</summary>
	private sealed record Seed(
		Guid TenantA,
		Guid TenantB,
		Guid RoleA,
		Guid RoleB,
		Guid Person,
		Guid OwnerOfB,
		Guid GroupMappingA,
		Guid GroupMappingB);

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		var tenantA = await IdentitySeed.TenantAsync(factory);
		var tenantB = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantA, "perfil-a", "logs:read");
		await IdentitySeed.RoleAsync(factory, tenantB, "perfil-b", "logs:write");

		// A pessoa é dona em A (com o perfil de A) e membro em B (com o perfil de B)
		var person = await IdentitySeed.UserAsync(factory, tenantA, $"pessoa-{Guid.NewGuid():N}@teste.local", "perfil-a");
		var ownerOfB = await IdentitySeed.UserAsync(factory, tenantB, $"dono-b-{Guid.NewGuid():N}@teste.local", "perfil-b");

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var roleA = await context.Roles.SingleAsync(r => r.TenantId == tenantA && r.NormalizedName == "PERFIL-A");
		var roleB = await context.Roles.SingleAsync(r => r.TenantId == tenantB && r.NormalizedName == "PERFIL-B");

		context.UserRoles.Add(new UserRole { UserId = person, RoleId = roleB.Id });
		context.TenantMemberships.AddRange(
			new TenantMembership(person, tenantA, DateTimeOffset.UtcNow),
			new TenantMembership(person, tenantB, DateTimeOffset.UtcNow),
			new TenantMembership(ownerOfB, tenantB, DateTimeOffset.UtcNow));
		context.TenantDatabases.AddRange(
			new TenantDatabase(tenantA, Product, "Server=a;Database=a"),
			new TenantDatabase(tenantB, Product, "Server=b;Database=b"));
		context.TenantFederations.AddRange(
			new TenantFederation(tenantA, Guid.NewGuid()),
			new TenantFederation(tenantB, Guid.NewGuid()));

		var mappingA = new TenantGroupRoleMapping(tenantA, Guid.NewGuid(), "Grupo A", roleA.Id);
		var mappingB = new TenantGroupRoleMapping(tenantB, Guid.NewGuid(), "Grupo B", roleB.Id);
		context.TenantGroupRoleMappings.AddRange(mappingA, mappingB);

		var now = DateTimeOffset.UtcNow;
		context.ElevationGrants.AddRange(
			new ElevationGrant(person, tenantA, "teste", null, now),
			new ElevationGrant(ownerOfB, tenantB, "teste", null, now));

		await context.SaveChangesAsync();

		_s = new Seed(tenantA, tenantB, roleA.Id, roleB.Id, person, ownerOfB, mappingA.Id, mappingB.Id);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	/// <summary>Executa <paramref name="body"/> numa scope de DI NOVA, no estado de escopo pedido (nulo = nenhum).</summary>
	private async Task InScopeAsync(
		Func<ITenantAccessScope, IDisposable>? enter,
		Func<SecureGateDbContext, IServiceProvider, Task> body)
	{
		using var diScope = factory.Services.CreateScope();
		var accessScope = diScope.ServiceProvider.GetRequiredService<ITenantAccessScope>();
		using var handle = enter?.Invoke(accessScope);
		var context = diScope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		await body(context, diScope.ServiceProvider);
	}

	private static Func<ITenantAccessScope, IDisposable> Tenant(Guid tenantId) => scope => scope.EnterTenant(tenantId);

	private static Func<ITenantAccessScope, IDisposable> Platform() => scope => scope.EnterPlatform("teste de isolamento");

	// ---- Itens 1: cada entidade filtrada ----

	[Fact]
	public async Task Filtros_EscopoDoTenantA_NaoMostramNadaDeB()
	{
		await InScopeAsync(Tenant(_s.TenantA), async (context, _) =>
		{
			var roles = await context.Roles.AsNoTracking().ToListAsync();
			roles.Should().NotBeEmpty().And.OnlyContain(r => r.TenantId == _s.TenantA);
			roles.Select(r => r.Id).Should().Contain(_s.RoleA).And.NotContain(_s.RoleB);

			var roleClaims = await context.RoleClaims.AsNoTracking().ToListAsync();
			roleClaims.Should().NotBeEmpty().And.OnlyContain(c => c.RoleId != _s.RoleB);
			roleClaims.Select(c => c.RoleId).Should().Contain(_s.RoleA);

			var userRoles = await context.UserRoles.AsNoTracking().ToListAsync();
			userRoles.Should().NotBeEmpty().And.OnlyContain(ur => ur.RoleId != _s.RoleB);

			var memberships = await context.TenantMemberships.AsNoTracking().ToListAsync();
			memberships.Should().NotBeEmpty().And.OnlyContain(m => m.TenantId == _s.TenantA);

			var users = await context.Users.AsNoTracking().ToListAsync();
			users.Select(u => u.Id).Should().Contain(_s.Person).And.NotContain(_s.OwnerOfB);

			var databases = await context.TenantDatabases.AsNoTracking().ToListAsync();
			databases.Should().ContainSingle().Which.TenantId.Should().Be(_s.TenantA);

			var federations = await context.TenantFederations.AsNoTracking().ToListAsync();
			federations.Should().ContainSingle().Which.TenantId.Should().Be(_s.TenantA);

			var mappings = await context.TenantGroupRoleMappings.AsNoTracking().ToListAsync();
			mappings.Should().ContainSingle().Which.Id.Should().Be(_s.GroupMappingA);

			var grants = await context.ElevationGrants.AsNoTracking().ToListAsync();
			grants.Should().ContainSingle().Which.TenantId.Should().Be(_s.TenantA);
		});
	}

	[Fact]
	public async Task Filtros_SemEscopo_NaoMostramNada()
	{
		await InScopeAsync(null, async (context, _) =>
		{
			(await context.Roles.AsNoTracking().ToListAsync()).Should().BeEmpty();
			(await context.RoleClaims.AsNoTracking().ToListAsync()).Should().BeEmpty();
			(await context.UserRoles.AsNoTracking().ToListAsync()).Should().BeEmpty();
			(await context.TenantMemberships.AsNoTracking().ToListAsync()).Should().BeEmpty();
			(await context.Users.AsNoTracking().ToListAsync()).Should().BeEmpty();
			(await context.TenantDatabases.AsNoTracking().ToListAsync()).Should().BeEmpty();
			(await context.TenantFederations.AsNoTracking().ToListAsync()).Should().BeEmpty();
			(await context.TenantGroupRoleMappings.AsNoTracking().ToListAsync()).Should().BeEmpty();
			(await context.ElevationGrants.AsNoTracking().ToListAsync()).Should().BeEmpty();
		});
	}

	[Fact]
	public async Task Filtros_ModoPlataforma_MostramOsDoisTenants()
	{
		await InScopeAsync(Platform(), async (context, _) =>
		{
			(await context.Roles.AsNoTracking().Select(r => r.Id).ToListAsync()).Should().Contain([_s.RoleA, _s.RoleB]);
			(await context.RoleClaims.AsNoTracking().Select(c => c.RoleId).ToListAsync()).Should().Contain([_s.RoleA, _s.RoleB]);
			(await context.UserRoles.AsNoTracking().Select(ur => ur.RoleId).ToListAsync()).Should().Contain([_s.RoleA, _s.RoleB]);
			(await context.TenantMemberships.AsNoTracking().Select(m => m.TenantId).ToListAsync()).Should().Contain([_s.TenantA, _s.TenantB]);
			(await context.Users.AsNoTracking().Select(u => u.Id).ToListAsync()).Should().Contain([_s.Person, _s.OwnerOfB]);
			(await context.TenantDatabases.AsNoTracking().Select(d => d.TenantId).ToListAsync()).Should().Contain([_s.TenantA, _s.TenantB]);
			(await context.TenantFederations.AsNoTracking().Select(f => f.TenantId).ToListAsync()).Should().Contain([_s.TenantA, _s.TenantB]);
			(await context.TenantGroupRoleMappings.AsNoTracking().Select(m => m.Id).ToListAsync()).Should().Contain([_s.GroupMappingA, _s.GroupMappingB]);
			(await context.ElevationGrants.AsNoTracking().Select(g => g.TenantId).ToListAsync()).Should().Contain([_s.TenantA, _s.TenantB]);
		});
	}

	[Fact]
	public async Task Filtros_EntidadesForaDoFiltro_SeguemVisiveisSemEscopo()
	{
		// Tenant (catálogo da instalação) e o catálogo de permissões são dado de plataforma, sem tenant
		await InScopeAsync(null, async (context, _) =>
		{
			(await context.Tenants.AsNoTracking().Select(t => t.Id).ToListAsync()).Should().Contain([_s.TenantA, _s.TenantB]);
		});
	}

	// ---- Item 2: a pessoa (dona OU membro) ----

	[Fact]
	public async Task Pessoa_DonaEmAMembroEmB_AparecePeloEscopoDeCadaUm()
	{
		await InScopeAsync(Tenant(_s.TenantA), async (context, _) =>
			(await context.Users.AsNoTracking().Select(u => u.Id).ToListAsync()).Should().Contain(_s.Person));

		await InScopeAsync(Tenant(_s.TenantB), async (context, _) =>
		{
			var ids = await context.Users.AsNoTracking().Select(u => u.Id).ToListAsync();
			ids.Should().Contain([_s.Person, _s.OwnerOfB], "dona em A mas membro em B, e o dono de B");
		});
	}

	[Fact]
	public async Task Pessoa_TenantSemVinculoNemPosse_NaoAparece()
	{
		var tenantC = await IdentitySeed.TenantAsync(factory);

		await InScopeAsync(Tenant(tenantC), async (context, _) =>
			(await context.Users.AsNoTracking().Select(u => u.Id).ToListAsync())
				.Should().NotContain([_s.Person, _s.OwnerOfB]));
	}

	[Fact]
	public async Task Pessoa_VinculoRemovido_SomeDoTenantQueDeixouDeSerMembro()
	{
		await InScopeAsync(Platform(), async (context, _) =>
			await context.TenantMemberships
				.Where(m => m.UserId == _s.Person && m.TenantId == _s.TenantB)
				.ExecuteDeleteAsync());

		await InScopeAsync(Tenant(_s.TenantB), async (context, _) =>
			(await context.Users.AsNoTracking().Select(u => u.Id).ToListAsync())
				.Should().NotContain(_s.Person).And.Contain(_s.OwnerOfB));
	}

	// ---- Item 3: perfis de outro tenant, inclusive pelo Identity ----

	[Fact]
	public async Task GetRolesAsync_EscopoDeB_NaoDevolvePerfilDeA()
	{
		await InScopeAsync(Tenant(_s.TenantB), async (_, services) =>
		{
			var userManager = services.GetRequiredService<UserManager<User>>();
			var person = await userManager.FindByIdAsync(_s.Person.ToString());

			person.Should().NotBeNull("a pessoa é membro de B");
			(await userManager.GetRolesAsync(person!)).Should().BeEquivalentTo(["perfil-b"]);
		});

		await InScopeAsync(Tenant(_s.TenantA), async (_, services) =>
		{
			var userManager = services.GetRequiredService<UserManager<User>>();
			var person = await userManager.FindByIdAsync(_s.Person.ToString());

			(await userManager.GetRolesAsync(person!)).Should().BeEquivalentTo(["perfil-a"]);
		});
	}

	[Fact]
	public async Task UserManager_SemEscopo_NaoAchaNinguemNemPerfil()
	{
		await InScopeAsync(null, async (_, services) =>
		{
			var userManager = services.GetRequiredService<UserManager<User>>();

			(await userManager.FindByIdAsync(_s.Person.ToString())).Should().BeNull();
			(await services.GetRequiredService<RoleManager<Role>>().FindByIdAsync(_s.RoleA.ToString())).Should().BeNull();
		});
	}

	// ---- Item 5: ExecuteDelete passa pelo filtro ----

	[Fact]
	public async Task ExecuteDelete_EscopoDeA_NaoApagaLinhaDeB()
	{
		await InScopeAsync(Tenant(_s.TenantA), async (context, _) =>
		{
			var deletedRoles = await context.Roles.Where(r => r.Id == _s.RoleB).ExecuteDeleteAsync();
			var deletedMemberships = await context.TenantMemberships
				.Where(m => m.UserId == _s.Person).ExecuteDeleteAsync();

			deletedRoles.Should().Be(0);
			deletedMemberships.Should().Be(1, "só o vínculo de A; o de B está fora do escopo");
		});

		await InScopeAsync(Platform(), async (context, _) =>
		{
			(await context.Roles.AnyAsync(r => r.Id == _s.RoleB)).Should().BeTrue();
			(await context.TenantMemberships.AnyAsync(m => m.UserId == _s.Person && m.TenantId == _s.TenantB))
				.Should().BeTrue();
			(await context.TenantMemberships.AnyAsync(m => m.UserId == _s.Person && m.TenantId == _s.TenantA))
				.Should().BeFalse();
		});
	}

	// ---- Item 6: a borda — cada grupo de rota com tenant lê só o tenant da rota ----

	private HttpClient Client(string scope)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(scope));

		return client;
	}

	private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
		await response.Content.ReadFromJsonAsync<JsonElement>(Json);

	[Fact]
	public async Task Borda_Roles_ListaSoOsDoTenantDaRota()
	{
		var response = await Client(SecureGateScopes.Admin).GetAsync($"/api/v1/tenants/{_s.TenantA}/roles");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var names = (await ReadAsync(response)).EnumerateArray().Select(r => r.GetProperty("name").GetString()).ToList();
		names.Should().Contain("perfil-a").And.NotContain("perfil-b");
	}

	[Fact]
	public async Task Borda_Users_ListaSoAPessoaDonaOuMembroDoTenantDaRota()
	{
		var admin = Client(SecureGateScopes.Admin);

		var inA = (await ReadAsync(await admin.GetAsync($"/api/v1/tenants/{_s.TenantA}/users")))
			.EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToList();
		var inB = (await ReadAsync(await admin.GetAsync($"/api/v1/tenants/{_s.TenantB}/users")))
			.EnumerateArray().Select(u => u.GetProperty("id").GetGuid()).ToList();

		inA.Should().Contain(_s.Person).And.NotContain(_s.OwnerOfB);
		inB.Should().Contain([_s.Person, _s.OwnerOfB]);
	}

	[Fact]
	public async Task Borda_Tenants_DetalheMostraSoOsBancosDoTenantDaRota()
	{
		var admin = Client(SecureGateScopes.Admin);

		var detailA = await ReadAsync(await admin.GetAsync($"/api/v1/tenants/{_s.TenantA}"));
		var tenantWithoutDatabase = await IdentitySeed.TenantAsync(factory);
		var detailC = await ReadAsync(await admin.GetAsync($"/api/v1/tenants/{tenantWithoutDatabase}"));

		detailA.GetProperty("products").EnumerateArray().Select(p => p.GetString()).Should().ContainSingle().Which.Should().Be(Product);
		detailA.GetProperty("federation").ValueKind.Should().Be(JsonValueKind.Object);
		detailC.GetProperty("products").GetArrayLength().Should().Be(0);
	}

	[Fact]
	public async Task Borda_Entra_ListaSoOsMapeamentosDoTenantDaRota()
	{
		var admin = Client(SecureGateScopes.Admin);

		var inA = await ReadAsync(await admin.GetAsync($"/api/v1/tenants/{_s.TenantA}/entra/group-role-mappings"));
		var inB = await ReadAsync(await admin.GetAsync($"/api/v1/tenants/{_s.TenantB}/entra/group-role-mappings"));

		inA.EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).Should().ContainSingle().Which.Should().Be(_s.GroupMappingA);
		inB.EnumerateArray().Select(m => m.GetProperty("id").GetGuid()).Should().ContainSingle().Which.Should().Be(_s.GroupMappingB);
	}

	[Fact]
	public async Task Borda_Elevacao_LeAConcessaoSoNoTenantDono()
	{
		var admin = Client(SecureGateScopes.Admin);

		var own = await admin.GetAsync($"/api/v1/tenants/{_s.TenantA}/users/{_s.Person}/elevation");
		var foreign = await admin.GetAsync($"/api/v1/tenants/{_s.TenantB}/users/{_s.Person}/elevation");

		own.StatusCode.Should().Be(HttpStatusCode.OK);
		(await ReadAsync(own)).GetProperty("tenantId").GetGuid().Should().Be(_s.TenantA);
		foreign.StatusCode.Should().Be(HttpStatusCode.NotFound, "a pessoa é dona em A; operação de pessoa só pelo dono (ADR-0040)");
	}

	[Fact]
	public async Task Borda_Clients_ListaNoTenantDaRotaSemErro()
	{
		var response = await Client(SecureGateScopes.Admin).GetAsync($"/api/v1/tenants/{_s.TenantA}/clients");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Borda_Membros_IncluiPessoaDeOutroDonoSoNoTenantDaRota()
	{
		var tenantC = await IdentitySeed.TenantAsync(factory);

		var response = await Client(SecureGateScopes.Admin)
			.PutAsync($"/api/v1/tenants/{tenantC}/members/{_s.Person}", content: null);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		await InScopeAsync(Platform(), async (context, _) =>
			(await context.TenantMemberships.AnyAsync(m => m.TenantId == tenantC && m.UserId == _s.Person)).Should().BeTrue());
	}

	[Fact]
	public async Task Borda_Autorizacao_ResolvePermissoesSoDoTenantDaRota()
	{
		var client = Client(SecureGateScopes.AuthorizationRead);

		var inA = await ReadAsync(await client.GetAsync($"/api/v1/authorization/tenants/{_s.TenantA}/roles/perfil-a/permissions"));
		var inB = await ReadAsync(await client.GetAsync($"/api/v1/authorization/tenants/{_s.TenantB}/roles/perfil-a/permissions"));

		inA.EnumerateArray().Select(p => p.GetString()).Should().BeEquivalentTo(["logs:read"]);
		inB.GetArrayLength().Should().Be(0, "o perfil 'perfil-a' não existe em B");
	}
}
