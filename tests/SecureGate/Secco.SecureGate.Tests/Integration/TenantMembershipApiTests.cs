using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Gestão de vínculos pessoa-tenant pela API (ADR-0040): inclusão por e-mail, PUT idempotente,
/// suspensão e remoção, com a revogação de sessão e a limpeza de perfis só do tenant.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class TenantMembershipApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _owner;
	private Guid _other;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_owner = await IdentitySeed.TenantAsync(factory);
		_other = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"membro-{Guid.NewGuid():N}@secco.test";

	private HttpClient Admin() => IdentitySeed.AdminClient(factory);

	private static string MembersUrl(Guid tenantId) => $"/api/v1/tenants/{tenantId}/members";

	private static string MemberUrl(Guid tenantId, Guid userId) => $"/api/v1/tenants/{tenantId}/members/{userId}";

	private async Task<string> StampAsync(Guid userId)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var user = await scope.ServiceProvider.GetRequiredService<UserManager<User>>().FindByIdAsync(userId.ToString());

		return SessionVersion.From(user!.SecurityStamp);
	}

	private async Task<bool> HasMembershipRowAsync(Guid tenantId, Guid userId)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.TenantMemberships.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId);
	}

	private async Task<bool?> MembershipActiveAsync(Guid tenantId, Guid userId)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.TenantMemberships
			.Where(m => m.TenantId == tenantId && m.UserId == userId)
			.Select(m => (bool?)m.IsActive)
			.FirstOrDefaultAsync();
	}

	/// <summary>Pessoa dona do tenant <c>_other</c>, já membro ativo do <c>_owner</c> pelo PUT.</summary>
	private async Task<Guid> MemberOfOwnerAsync(params string[] rolesInOther)
	{
		var userId = await IdentitySeed.UserAsync(factory, _other, Email(), rolesInOther);
		(await Admin().PutAsync(MemberUrl(_owner, userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		return userId;
	}

	// ---- POST (incluir por e-mail)

	[Fact]
	public async Task Post_PessoaExistente_Retorna201ComLocationEDto()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _other, email);

		var response = await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		response.Headers.Location!.ToString().Should().Be($"/api/v1/tenants/{_owner}/users/{userId}");
		var dto = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
		dto.GetProperty("userId").GetGuid().Should().Be(userId);
		dto.GetProperty("email").GetString().Should().Be(email);
		dto.GetProperty("ownerTenantId").GetGuid().Should().Be(_other);
		dto.GetProperty("isActive").GetBoolean().Should().BeTrue();
		(await MembershipActiveAsync(_owner, userId)).Should().BeTrue();
	}

	[Fact]
	public async Task Post_EmailEmCaixaDiferente_EncontraAPessoa()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _other, email);

		var response = await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email = email.ToUpperInvariant() }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.Created);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("isto-nao-e-email")]
	public async Task Post_EmailInvalido_Retorna400(string? email)
	{
		var response = await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Post_EmailAcimaDoLimite_Retorna400()
	{
		var email = new string('a', 250) + "@secco.test";

		var response = await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Post_TenantInexistente_Retorna404()
	{
		var email = Email();
		await IdentitySeed.UserAsync(factory, _other, email);

		var response = await Admin().PostAsJsonAsync(MembersUrl(Guid.NewGuid()), new { email }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Post_PessoaInexistente_Retorna404()
	{
		var response = await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email = Email() }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Post_JaMembroAtivo_Retorna409()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _other, email);
		(await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email }, Json)).StatusCode.Should().Be(HttpStatusCode.Created);

		var again = await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email }, Json);

		again.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_owner}/users", Json))
			.EnumerateArray().Count(u => u.GetProperty("id").GetGuid() == userId).Should().Be(1, "nunca um segundo vínculo");
	}

	[Fact]
	public async Task Post_JaMembroSuspenso_Retorna409()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _other, email);
		await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email }, Json);
		(await Admin().PostAsync(MemberUrl(_owner, userId) + "/suspend", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var again = await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email }, Json);

		again.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await MembershipActiveAsync(_owner, userId)).Should().BeFalse("o POST não reativa; quem reativa é o PUT");
	}

	[Fact]
	public async Task Post_Dono_Retorna409SemDependerDaLinhaDoDono()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _owner, email);
		(await HasMembershipRowAsync(_owner, userId)).Should().BeFalse("o seed de teste não cria a linha do dono");

		var response = await Admin().PostAsJsonAsync(MembersUrl(_owner), new { email }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Post_PlataformaComPessoaDeOutroDono_Retorna409()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _other, email);

		var response = await Admin().PostAsJsonAsync(MembersUrl(SecureGatePlatform.TenantId), new { email }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await HasMembershipRowAsync(SecureGatePlatform.TenantId, userId)).Should().BeFalse();
	}

	[Fact]
	public async Task Post_TenantInativo_Aceita()
	{
		var inactive = await IdentitySeed.TenantAsync(factory);
		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			(await context.Tenants.FirstAsync(t => t.Id == inactive)).Deactivate();
			await context.SaveChangesAsync();
		}

		var email = Email();
		await IdentitySeed.UserAsync(factory, _other, email);

		var response = await Admin().PostAsJsonAsync(MembersUrl(inactive), new { email }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.Created, "o admin prepara a reativação do tenant");
	}

	[Fact]
	public async Task Post_SemEscopoDeAdmin_Retorna403()
	{
		using var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.AuthorizationRead));

		var response = await client.PostAsJsonAsync(MembersUrl(_owner), new { email = Email() }, Json);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	// ---- PUT (incluir ou reativar)

	[Fact]
	public async Task Put_Inclui_EDepoisRepetido_Retorna204ComUmSoVinculo()
	{
		var userId = await IdentitySeed.UserAsync(factory, _other, Email());

		(await Admin().PutAsync(MemberUrl(_owner, userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await Admin().PutAsync(MemberUrl(_owner, userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await MembershipActiveAsync(_owner, userId)).Should().BeTrue();
	}

	[Fact]
	public async Task Put_VinculoSuspenso_Reativa()
	{
		var userId = await MemberOfOwnerAsync();
		await Admin().PostAsync(MemberUrl(_owner, userId) + "/suspend", null);
		(await MembershipActiveAsync(_owner, userId)).Should().BeFalse();

		(await Admin().PutAsync(MemberUrl(_owner, userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await MembershipActiveAsync(_owner, userId)).Should().BeTrue();
	}

	[Fact]
	public async Task Put_Dono_Retorna204SemCriarLinha()
	{
		var userId = await IdentitySeed.UserAsync(factory, _owner, Email());

		(await Admin().PutAsync(MemberUrl(_owner, userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await HasMembershipRowAsync(_owner, userId)).Should().BeFalse("o dono conta pela coluna");
	}

	[Fact]
	public async Task Put_TenantOuPessoaInexistente_Retorna404()
	{
		var userId = await IdentitySeed.UserAsync(factory, _other, Email());

		(await Admin().PutAsync(MemberUrl(Guid.NewGuid(), userId), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await Admin().PutAsync(MemberUrl(_owner, Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Put_PlataformaComPessoaDeOutroDono_Retorna409()
	{
		var userId = await IdentitySeed.UserAsync(factory, _other, Email());

		var response = await Admin().PutAsync(MemberUrl(SecureGatePlatform.TenantId, userId), null);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await HasMembershipRowAsync(SecureGatePlatform.TenantId, userId)).Should().BeFalse();
	}

	// ---- POST suspend

	[Fact]
	public async Task Suspend_Membro_Retorna204EncerraSessaoEMarcaSuspenso()
	{
		var userId = await MemberOfOwnerAsync();
		var before = await StampAsync(userId);

		var response = await Admin().PostAsync(MemberUrl(_owner, userId) + "/suspend", null);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await MembershipActiveAsync(_owner, userId)).Should().BeFalse();
		(await StampAsync(userId)).Should().NotBe(before, "suspender de fato revoga as sessões (sver muda)");
	}

	[Fact]
	public async Task Suspend_Repetido_Retorna204SemRevogarDeNovo()
	{
		var userId = await MemberOfOwnerAsync();
		await Admin().PostAsync(MemberUrl(_owner, userId) + "/suspend", null);
		var afterFirst = await StampAsync(userId);

		var again = await Admin().PostAsync(MemberUrl(_owner, userId) + "/suspend", null);

		again.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await StampAsync(userId)).Should().Be(afterFirst, "a chamada idempotente sem efeito não derruba ninguém");
	}

	[Fact]
	public async Task Suspend_NaoMembro_Retorna404()
	{
		var userId = await IdentitySeed.UserAsync(factory, _other, Email());

		(await Admin().PostAsync(MemberUrl(_owner, userId) + "/suspend", null)).StatusCode
			.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Suspend_PessoaOuTenantInexistente_Retorna404()
	{
		var userId = await IdentitySeed.UserAsync(factory, _other, Email());

		(await Admin().PostAsync(MemberUrl(_owner, Guid.NewGuid()) + "/suspend", null)).StatusCode
			.Should().Be(HttpStatusCode.NotFound);
		(await Admin().PostAsync(MemberUrl(Guid.NewGuid(), userId) + "/suspend", null)).StatusCode
			.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Suspend_Dono_Retorna409SemTocarNaSessao()
	{
		var userId = await IdentitySeed.UserAsync(factory, _owner, Email());
		var before = await StampAsync(userId);

		var response = await Admin().PostAsync(MemberUrl(_owner, userId) + "/suspend", null);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await StampAsync(userId)).Should().Be(before);
	}

	// ---- DELETE

	[Fact]
	public async Task Delete_Membro_RemoveVinculoEPerfisSoDesteTenant()
	{
		await IdentitySeed.RoleAsync(factory, _other, "colaborador");
		await IdentitySeed.RoleAsync(factory, _owner, "comprador");
		var userId = await MemberOfOwnerAsync("colaborador");
		(await Admin().PostAsync($"/api/v1/tenants/{_owner}/users/{userId}/roles/comprador", null)).StatusCode
			.Should().Be(HttpStatusCode.NoContent);
		var before = await StampAsync(userId);

		var response = await Admin().DeleteAsync(MemberUrl(_owner, userId));

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await HasMembershipRowAsync(_owner, userId)).Should().BeFalse();
		(await StampAsync(userId)).Should().NotBe(before, "remover de fato revoga as sessões");

		// O perfil do tenant dono da conta fica; o do tenant removido saiu
		var home = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_other}/users/{userId}", Json);
		home.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("colaborador");
	}

	[Fact]
	public async Task Delete_ReincluirDepois_NaoDevolvePerfilEsquecido()
	{
		await IdentitySeed.RoleAsync(factory, _owner, "comprador");
		var userId = await MemberOfOwnerAsync();
		await Admin().PostAsync($"/api/v1/tenants/{_owner}/users/{userId}/roles/comprador", null);
		(await Admin().DeleteAsync(MemberUrl(_owner, userId))).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await Admin().PutAsync(MemberUrl(_owner, userId), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var detail = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_owner}/users/{userId}", Json);
		detail.GetProperty("roles").GetArrayLength().Should().Be(0, "reincluir não devolve acesso esquecido");
	}

	[Fact]
	public async Task Delete_VinculoSuspenso_AlemDeRemoverApagaOsPerfis()
	{
		await IdentitySeed.RoleAsync(factory, _owner, "comprador");
		var userId = await MemberOfOwnerAsync();
		await Admin().PostAsync($"/api/v1/tenants/{_owner}/users/{userId}/roles/comprador", null);
		await Admin().PostAsync(MemberUrl(_owner, userId) + "/suspend", null);

		(await Admin().DeleteAsync(MemberUrl(_owner, userId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		await Admin().PutAsync(MemberUrl(_owner, userId), null);

		var detail = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_owner}/users/{userId}", Json);
		detail.GetProperty("roles").GetArrayLength().Should().Be(0);
	}

	[Fact]
	public async Task Delete_NaoMembro_Retorna204SemRevogarSessao()
	{
		var userId = await IdentitySeed.UserAsync(factory, _other, Email());
		var before = await StampAsync(userId);

		var response = await Admin().DeleteAsync(MemberUrl(_owner, userId));

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await StampAsync(userId)).Should().Be(before, "quem não era membro não tem nada a encerrar");
	}

	[Fact]
	public async Task Delete_PessoaOuTenantInexistente_Retorna404()
	{
		var userId = await IdentitySeed.UserAsync(factory, _other, Email());

		(await Admin().DeleteAsync(MemberUrl(_owner, Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await Admin().DeleteAsync(MemberUrl(Guid.NewGuid(), userId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Delete_Dono_Retorna409ESeusPerfisFicam()
	{
		await IdentitySeed.RoleAsync(factory, _owner, "colaborador");
		var userId = await IdentitySeed.UserAsync(factory, _owner, Email(), "colaborador");

		var response = await Admin().DeleteAsync(MemberUrl(_owner, userId));

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		var detail = await Admin().GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_owner}/users/{userId}", Json);
		detail.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).Should().Equal("colaborador");
	}
}
