using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Rotas de usuário do tenant diante de pessoas com vínculo (ADR-0040): listar, detalhar e atribuir
/// perfil passam a valer para membros; as operações de pessoa continuam só do tenant dono.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class UserRoutesForMembersTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	/// <summary>Tenant dono da conta da pessoa.</summary>
	private Guid _home;

	/// <summary>Tenant de que a pessoa é apenas membro.</summary>
	private Guid _guest;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_home = await IdentitySeed.TenantAsync(factory);
		_guest = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"rota-{Guid.NewGuid():N}@secco.test";

	private HttpClient Admin() => IdentitySeed.AdminClient(factory);

	private static IReadOnlyList<string?> Strings(JsonElement element, string property) =>
		[.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString())];

	private async Task AddMembershipAsync(Guid tenantId, Guid userId, bool active = true)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var membership = new TenantMembership(userId, tenantId, DateTimeOffset.UtcNow);

		if (!active)
		{
			membership.Suspend();
		}

		context.TenantMemberships.Add(membership);
		await context.SaveChangesAsync();
	}

	private async Task AssignAsync(Guid userId, Guid tenantId, string roleName)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var normalized = roleName.ToUpperInvariant();
		var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized);
		context.UserRoles.Add(new UserRole { UserId = userId, RoleId = role.Id });
		await context.SaveChangesAsync();
	}

	private async Task<string> StampAsync(Guid userId)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString());

		return SessionVersion.From(user!.SecurityStamp);
	}

	/// <summary>Pessoa dona de <c>_home</c> com perfil nos dois tenants e vínculo ativo em <c>_guest</c>.</summary>
	private async Task<Guid> PersonWithRolesInBothAsync()
	{
		await IdentitySeed.RoleAsync(factory, _home, "colaborador");
		await IdentitySeed.RoleAsync(factory, _guest, "comprador");
		var userId = await IdentitySeed.UserAsync(factory, _home, Email(), "colaborador");
		await AddMembershipAsync(_guest, userId);
		await AssignAsync(userId, _guest, "comprador");

		return userId;
	}

	// ---- ListUsers

	[Fact]
	public async Task ListUsers_MembroEDono_VeAmbosComPerfisSoDoTenantDaRota()
	{
		var userId = await PersonWithRolesInBothAsync();
		var guestOwner = await IdentitySeed.UserAsync(factory, _guest, Email(), "comprador");

		var guestList = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_guest}/users", Json);
		var homeList = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_home}/users", Json);

		var member = guestList.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == userId);
		Strings(member, "roles").Should().Equal("comprador");
		member.GetProperty("membership").GetString().Should().Be("Member");
		member.GetProperty("ownerTenantId").GetGuid().Should().Be(_home);
		member.GetProperty("tenantId").GetGuid().Should().Be(_guest, "o tenant da listagem");

		var owner = guestList.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == guestOwner);
		owner.GetProperty("membership").GetString().Should().Be("Owner");

		var atHome = homeList.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == userId);
		Strings(atHome, "roles").Should().Equal(["colaborador"], "o perfil do outro tenant não vaza para esta lista");
		atHome.GetProperty("membership").GetString().Should().Be("Owner");
	}

	[Fact]
	public async Task ListUsers_ComLinhaDoDono_NaoDuplicaODono()
	{
		var userId = await IdentitySeed.UserAsync(factory, _home, Email());
		await AddMembershipAsync(_home, userId);

		var list = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_home}/users", Json);

		list.EnumerateArray().Count(u => u.GetProperty("id").GetGuid() == userId).Should().Be(1);
	}

	[Fact]
	public async Task ListUsers_VinculoSuspenso_AparecePeloEstado()
	{
		var userId = await IdentitySeed.UserAsync(factory, _home, Email());
		await AddMembershipAsync(_guest, userId, active: false);

		var list = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_guest}/users", Json);

		list.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == userId)
			.GetProperty("membership").GetString().Should().Be("Suspended");
	}

	[Fact]
	public async Task ListUsers_PessoaSemVinculo_NaoApareceNoTenant()
	{
		var userId = await IdentitySeed.UserAsync(factory, _home, Email());

		var list = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_guest}/users", Json);

		list.EnumerateArray().Should().NotContain(u => u.GetProperty("id").GetGuid() == userId);
	}

	// ---- GetUser

	[Fact]
	public async Task GetUser_Membro_ZeraCredencialEMostraSoOsPerfisDoTenant()
	{
		var userId = await PersonWithRolesInBothAsync();

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			context.UserLogins.Add(new UserLogin
			{
				UserId = userId,
				LoginProvider = "EntraId",
				ProviderKey = $"{Guid.NewGuid()}:{Guid.NewGuid()}",
				ProviderDisplayName = "Microsoft",
			});
			await context.SaveChangesAsync();
		}

		var asMember = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_guest}/users/{userId}", Json);
		var asOwner = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_home}/users/{userId}", Json);

		asMember.GetProperty("membership").GetString().Should().Be("Member");
		asMember.GetProperty("ownerTenantId").GetGuid().Should().Be(_home, "o membro mostra de onde vem a conta");
		asMember.GetProperty("tenantId").GetGuid().Should().Be(_guest);
		Strings(asMember, "roles").Should().Equal("comprador");
		asMember.GetProperty("hasPassword").GetBoolean().Should().BeFalse("credencial é da pessoa");
		asMember.GetProperty("localLoginEnabled").GetBoolean().Should().BeFalse();
		asMember.GetProperty("twoFactorEnabled").GetBoolean().Should().BeFalse();
		Strings(asMember, "externalLogins").Should().BeEmpty();

		asOwner.GetProperty("membership").GetString().Should().Be("Owner");
		asOwner.GetProperty("ownerTenantId").GetGuid().Should().Be(_home);
		Strings(asOwner, "roles").Should().Equal("colaborador");
		asOwner.GetProperty("hasPassword").GetBoolean().Should().BeTrue("o dono continua vendo a credencial");
		Strings(asOwner, "externalLogins").Should().Equal("EntraId");
	}

	[Fact]
	public async Task GetUser_MembroSuspenso_Aceita()
	{
		var userId = await IdentitySeed.UserAsync(factory, _home, Email());
		await AddMembershipAsync(_guest, userId, active: false);

		var response = await Admin().GetAsync($"/api/v1/tenants/{_guest}/users/{userId}");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await response.Content.ReadFromJsonAsync<JsonElement>(Json))
			.GetProperty("membership").GetString().Should().Be("Suspended");
	}

	[Fact]
	public async Task GetUser_SemVinculo_Retorna404()
	{
		var userId = await IdentitySeed.UserAsync(factory, _home, Email());

		(await Admin().GetAsync($"/api/v1/tenants/{_guest}/users/{userId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	// ---- AddUserRole / RemoveUserRole

	[Fact]
	public async Task AddUserRole_MembroAtivoESuspenso_Atribui()
	{
		await IdentitySeed.RoleAsync(factory, _guest, "comprador");
		var active = await IdentitySeed.UserAsync(factory, _home, Email());
		var suspended = await IdentitySeed.UserAsync(factory, _home, Email());
		await AddMembershipAsync(_guest, active);
		await AddMembershipAsync(_guest, suspended, active: false);

		(await Admin().PostAsync($"/api/v1/tenants/{_guest}/users/{active}/roles/comprador", null)).StatusCode
			.Should().Be(HttpStatusCode.NoContent);
		(await Admin().PostAsync($"/api/v1/tenants/{_guest}/users/{suspended}/roles/comprador", null)).StatusCode
			.Should().Be(HttpStatusCode.NoContent);

		var detail = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_guest}/users/{active}", Json);
		Strings(detail, "roles").Should().Equal("comprador");
	}

	[Fact]
	public async Task AddUserRole_Dono_ContinuaAceitoPelaColuna()
	{
		await IdentitySeed.RoleAsync(factory, _home, "colaborador");
		var userId = await IdentitySeed.UserAsync(factory, _home, Email());

		(await Admin().PostAsync($"/api/v1/tenants/{_home}/users/{userId}/roles/colaborador", null)).StatusCode
			.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task AddUserRole_NaoMembro_Retorna409ESemPerfilAtribuido()
	{
		await IdentitySeed.RoleAsync(factory, _guest, "comprador");
		var userId = await IdentitySeed.UserAsync(factory, _home, Email());

		var response = await Admin().PostAsync($"/api/v1/tenants/{_guest}/users/{userId}/roles/comprador", null);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.UserRoles.AnyAsync(ur => ur.UserId == userId)).Should().BeFalse();
	}

	[Fact]
	public async Task AddUserRole_PessoaInexistente_ContinuaRetornando404()
	{
		await IdentitySeed.RoleAsync(factory, _guest, "comprador");

		var response = await Admin().PostAsync($"/api/v1/tenants/{_guest}/users/{Guid.NewGuid()}/roles/comprador", null);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task RemoveUserRole_Membro_Retira()
	{
		var userId = await PersonWithRolesInBothAsync();

		(await Admin().DeleteAsync($"/api/v1/tenants/{_guest}/users/{userId}/roles/comprador")).StatusCode
			.Should().Be(HttpStatusCode.NoContent);

		var detail = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_guest}/users/{userId}", Json);
		Strings(detail, "roles").Should().BeEmpty();
	}

	[Fact]
	public async Task RemoveUserRole_NaoMembro_Retorna409()
	{
		await IdentitySeed.RoleAsync(factory, _guest, "comprador");
		var userId = await IdentitySeed.UserAsync(factory, _home, Email());

		(await Admin().DeleteAsync($"/api/v1/tenants/{_guest}/users/{userId}/roles/comprador")).StatusCode
			.Should().Be(HttpStatusCode.Conflict);
	}

	// ---- Operações de pessoa: só o tenant dono

	[Fact]
	public async Task OperacoesDePessoa_PeloTenantDeMembro_Retornam404SemEfeito()
	{
		var userId = await PersonWithRolesInBothAsync();
		var stampBefore = await StampAsync(userId);
		var admin = Admin();
		var @base = $"/api/v1/tenants/{_guest}/users/{userId}";

		static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

		var calls = new (string Name, Func<Task<HttpResponseMessage>> Send)[]
		{
			("convite", () => admin.PostAsync(@base + "/invite", null)),
			("reset de senha", () => admin.PostAsync(@base + "/password-reset", null)),
			("login local", () => admin.PostAsync(@base + "/local-login", Body("{\"enabled\":false}"))),
			("nome de exibição", () => admin.PutAsync(@base + "/display-name", Body("{\"displayName\":\"Invasor\"}"))),
			("reset do 2FA", () => admin.PostAsync(@base + "/two-factor/reset", null)),
			("login externo", () => admin.DeleteAsync(@base + "/external-logins/EntraId")),
			("desativar", () => admin.PostAsync(@base + "/deactivate", null)),
			("ativar", () => admin.PostAsync(@base + "/activate", null)),
			("encerrar sessões", () => admin.PostAsync(@base + "/sessions/revoke", null)),
			("elevação (conceder)", () => admin.PutAsync(@base + "/elevation", Body("{}"))),
			("elevação (consultar)", () => admin.GetAsync(@base + "/elevation")),
			("elevação (revogar)", () => admin.DeleteAsync(@base + "/elevation")),
		};

		foreach (var (name, send) in calls)
		{
			using var response = await send();
			response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{name} só vale pelo tenant dono");
		}

		// Nada mudou na conta: ativa, mesmo nome, mesma versão de sessão, com senha, sem concessão
		var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_home}/users/{userId}", Json);
		detail.GetProperty("status").GetString().Should().Be("Active");
		detail.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
		detail.GetProperty("hasPassword").GetBoolean().Should().BeTrue();
		detail.GetProperty("localLoginEnabled").GetBoolean().Should().BeTrue();
		(await StampAsync(userId)).Should().Be(stampBefore);

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.ElevationGrants.AnyAsync(g => g.UserId == userId)).Should().BeFalse();
	}
}
