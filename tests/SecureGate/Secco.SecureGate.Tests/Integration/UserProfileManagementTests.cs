using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Detalhe do usuário, atribuição e remoção de perfis (issue #26).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class UserProfileManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string Email() => $"usuario-{Guid.NewGuid():N}@secco.test";

	private Task<JsonElement> GetUserAsync(Guid userId) =>
		IdentitySeed.AdminClient(factory).GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/users/{userId}", Json);

	private static IReadOnlyList<string?> Strings(JsonElement element, string property) =>
		[.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString())];

	[Fact]
	public async Task GetUser_Ativo_DevolvePerfisEPermissoesEfetivasSemRepeticao()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "leitor", "documentos:read");
		await IdentitySeed.RoleAsync(factory, _tenantId, "editor", "documentos:read", "documentos:write");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email(), "leitor", "editor");

		var user = await GetUserAsync(userId);

		user.GetProperty("status").GetString().Should().Be("Active");
		user.GetProperty("lockoutEnd").ValueKind.Should().Be(JsonValueKind.Null);
		Strings(user, "roles").Should().BeEquivalentTo("editor", "leitor");
		Strings(user, "effectivePermissions").Should().Equal("documentos:read", "documentos:write");
	}

	[Fact]
	public async Task GetUser_PermissoesEfetivas_IguaisAResolucaoDosProdutos()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "auditor-interno", "relatorios:read", "trilha:read");
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email(), "auditor-interno");

		using var resolver = factory.CreateClient();
		resolver.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.AuthorizationRead));
		var resolved = await resolver.GetFromJsonAsync<List<string>>(
			$"/api/v1/authorization/tenants/{_tenantId}/roles/auditor-interno/permissions", Json);

		Strings(await GetUserAsync(userId), "effectivePermissions").Should().BeEquivalentTo(resolved);
	}

	[Fact]
	public async Task GetUser_Desativado_Deactivated()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.DeactivateAsync(factory, userId);

		(await GetUserAsync(userId)).GetProperty("status").GetString().Should().Be("Deactivated");
	}

	[Fact]
	public async Task GetUser_BloqueadoPorTentativas_LockedOutComFim()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.LockOutAsync(factory, userId, DateTimeOffset.UtcNow.AddMinutes(5));

		var user = await GetUserAsync(userId);

		user.GetProperty("status").GetString().Should().Be("LockedOut");
		user.GetProperty("lockoutEnd").ValueKind.Should().Be(JsonValueKind.String);
	}

	[Fact]
	public async Task GetUser_ComLoginEntra_ListaSoOProvedor()
	{
		const string providerKey = "11111111-2222-3333-4444-555555555555:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());

		using (var scope = factory.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			context.UserLogins.Add(new UserLogin { UserId = userId, LoginProvider = "EntraId", ProviderKey = providerKey, ProviderDisplayName = "Microsoft" });
			await context.SaveChangesAsync();
		}

		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/users/{userId}");
		var body = await response.Content.ReadAsStringAsync();

		body.Should().NotContain("aaaaaaaa-bbbb", "o identificador do diretório nunca sai da API");
		Strings(JsonDocument.Parse(body).RootElement, "externalLogins").Should().Equal("EntraId");
	}

	[Fact]
	public async Task GetUser_DeOutroTenant_Retorna404()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		var stranger = await IdentitySeed.UserAsync(factory, otherTenant, Email());

		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/users/{stranger}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task GetUser_Inexistente_Retorna404()
	{
		var response = await IdentitySeed.AdminClient(factory).GetAsync($"/api/v1/tenants/{_tenantId}/users/{Guid.CreateVersion7()}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task ListUsers_MostraSituacao()
	{
		var deactivated = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await IdentitySeed.DeactivateAsync(factory, deactivated);

		var users = await IdentitySeed.AdminClient(factory)
			.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/users", Json);

		users.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == deactivated)
			.GetProperty("status").GetString().Should().Be("Deactivated");
	}
}
