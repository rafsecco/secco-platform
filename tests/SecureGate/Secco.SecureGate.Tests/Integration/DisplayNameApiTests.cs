using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Nome de exibição via API de gestão (#30): criar já com nome, e um administrador definindo ou
/// limpando o de um usuário existente.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class DisplayNameApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static string UniqueEmail() => $"nome-{Guid.NewGuid():N}@secco.test";

	private HttpClient AdminClient()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.Admin));

		return client;
	}

	private async Task<Guid> TenantAsync(HttpClient admin)
	{
		var response = await admin.PostAsJsonAsync("/api/v1/tenants",
			new { name = "Tenant de nomes", slug = $"t-{Guid.NewGuid():N}" });
		response.StatusCode.Should().Be(HttpStatusCode.Created);

		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
	}

	[Fact]
	public async Task CreateUser_ComNomeDeExibicao_RefleteNoDto()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);

		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
			new { email = UniqueEmail(), displayName = "  Ana Cláudia  " });

		created.StatusCode.Should().Be(HttpStatusCode.Created);
		var body = await created.Content.ReadFromJsonAsync<JsonElement>(Json);

		// Aparado na entrada: o espaço extra do formulário não vira parte do nome.
		body.GetProperty("displayName").GetString().Should().Be("Ana Cláudia");
	}

	[Fact]
	public async Task CreateUser_SemNomeDeExibicao_VemNulo()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);

		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users", new { email = UniqueEmail() });

		created.StatusCode.Should().Be(HttpStatusCode.Created);
		var body = await created.Content.ReadFromJsonAsync<JsonElement>(Json);

		// Sem valor, o campo é nulo — nada quebra para quem não usa o recurso.
		body.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Fact]
	public async Task CreateUser_ComCaractereDeControleNoNome_Responde400()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);

		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
			new { email = UniqueEmail(), displayName = "Ana\r\nCláudia" });

		created.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Admin_DefineNomeDeExibicao_RefleteNoGetUser()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users", new { email = UniqueEmail() });
		var userId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		var response = await admin.PutAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/users/{userId}/display-name", new { displayName = "Rafael Secco" });

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/users/{userId}", Json);
		detail.GetProperty("displayName").GetString().Should().Be("Rafael Secco");
	}

	[Fact]
	public async Task Admin_LimpaNomeDeExibicaoComValorEmBranco()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
			new { email = UniqueEmail(), displayName = "Nome Inicial" });
		var userId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		var response = await admin.PutAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/users/{userId}/display-name", new { displayName = "   " });

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/users/{userId}", Json);
		detail.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Fact]
	public async Task Admin_DefineNomeDeExibicao_DeOutroTenant_Responde404()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		var outroTenantId = await TenantAsync(admin);
		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users", new { email = UniqueEmail() });
		var userId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		var response = await admin.PutAsJsonAsync(
			$"/api/v1/tenants/{outroTenantId}/users/{userId}/display-name", new { displayName = "Invasor" });

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);

		// A rota de outro tenant não pode ter deixado rastro na conta.
		var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/users/{userId}", Json);
		detail.GetProperty("displayName").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Fact]
	public async Task Admin_NomeComCaractereDeControle_Responde400()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users", new { email = UniqueEmail() });
		var userId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		var response = await admin.PutAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/users/{userId}/display-name", new { displayName = "Ana\tCláudia" });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task RoleMembro_MostraNomeDeExibicao()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		var created = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
			new { email = UniqueEmail(), displayName = "Membro Um" });
		var userId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();

		(await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name = "leitor" }))
			.StatusCode.Should().Be(HttpStatusCode.Created);
		(await admin.PostAsync($"/api/v1/tenants/{tenantId}/users/{userId}/roles/leitor", null))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var members = await admin.GetFromJsonAsync<JsonElement>(
			$"/api/v1/tenants/{tenantId}/roles/leitor/members", Json);

		members.GetProperty("items").EnumerateArray().Should().ContainSingle()
			.Which.GetProperty("displayName").GetString().Should().Be("Membro Um");
	}
}
