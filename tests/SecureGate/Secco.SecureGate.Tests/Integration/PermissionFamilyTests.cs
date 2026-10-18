using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Famílias de permissão no catálogo (ADR-0043): publicação de <c>prefixo-{param}:acao</c>, aceite de
/// instância pelo <c>SetRolePermissions</c>, ação fixa, e família nunca órfã na leitura do perfil
/// nem na limpeza por tenant. Prefixos únicos por teste — o catálogo é global e a suíte compartilha o banco.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class PermissionFamilyTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private HttpClient _admin = null!;
	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_admin = IdentitySeed.AdminClient(factory);
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync()
	{
		_admin.Dispose();

		return Task.CompletedTask;
	}

	private static string NewProduct() => $"p-{Guid.NewGuid():N}"[..24];

	private static string NewPrefix() => $"fam{Guid.NewGuid():N}"[..14];

	private async Task<HttpResponseMessage> PublishAsync(string product, params (string Name, string Description)[] permissions)
	{
		using var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(SecureGateScopes.PermissionsFor(product)));

		return await client.PutAsJsonAsync($"/api/v1/permission-catalog/{product}",
			new { permissions = permissions.Select(p => new { name = p.Name, description = p.Description }).ToArray() });
	}

	private Task<HttpResponseMessage> SetAsync(string role, params string[] permissions) =>
		_admin.PutAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles/{role}/permissions", new { permissions });

	private async Task CreateRoleAsync(string role) =>
		(await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/roles", new { name = role }))
			.StatusCode.Should().Be(HttpStatusCode.Created);

	[Fact]
	public async Task Publish_Family_IsReadWithIsFamilyTrue_AndExactNameWithFalse()
	{
		var product = NewProduct();
		var prefix = NewPrefix();

		(await PublishAsync(product, ($"{prefix}-{{id}}:read", "Ler o setor {id}"), ($"{prefix}s:admin", "Administrar")))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);
		var catalog = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/permission-catalog/{product}", Json);

		var byName = catalog.GetProperty("permissions").EnumerateArray()
			.ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("isFamily").GetBoolean());
		byName.Should().Contain($"{prefix}-{{id}}:read", true).And.Contain($"{prefix}s:admin", false);
	}

	[Theory]
	[InlineData("{x}:read")]
	[InlineData("setor-{id}:{acao}")]
	[InlineData("setor-{id}-{x}:read")]
	[InlineData("a-{x}-b:read")]
	public async Task Publish_InvalidFamily_Returns400(string name)
	{
		var response = await PublishAsync(NewProduct(), (name, "Inválida"));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task SetPermissions_InstanceOfPublishedFamily_Returns204_ButOtherActionIs400()
	{
		var prefix = NewPrefix();
		(await PublishAsync(NewProduct(), ($"{prefix}-{{id}}:read", "Ler"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		await CreateRoleAsync("leitor-setor");

		var accepted = await SetAsync("leitor-setor", $"{prefix}-rh:read", $"{prefix}-recursos-humanos:read");
		var rejected = await SetAsync("leitor-setor", $"{prefix}-rh:read", $"{prefix}-rh:delete");
		var rejectedBody = await rejected.Content.ReadAsStringAsync();

		accepted.StatusCode.Should().Be(HttpStatusCode.NoContent);
		rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		rejectedBody.Should().Contain("PermissionsNotInCatalog").And.Contain($"{prefix}-rh:delete");
	}

	[Fact]
	public async Task SetPermissions_FamilyDeclarationItselfAndBarePrefix_AreRejected()
	{
		var prefix = NewPrefix();
		(await PublishAsync(NewProduct(), ($"{prefix}-{{id}}:read", "Ler"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		await CreateRoleAsync("estrito-familia");

		var family = await SetAsync("estrito-familia", $"{prefix}-{{id}}:read");
		var bare = await SetAsync("estrito-familia", $"{prefix}-:read");

		family.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a declaração da família não é nome de permissão (formato)");
		bare.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task GetRole_WithFamilyInstance_HasNoUnknownPermissions()
	{
		var prefix = NewPrefix();
		(await PublishAsync(NewProduct(), ($"{prefix}-{{id}}:read", "Ler"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		await IdentitySeed.RoleAsync(factory, _tenantId, "com-familia", $"{prefix}-rh:read", "fantasma-familia:read");

		var role = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles/com-familia", Json);

		role.GetProperty("unknownPermissions").EnumerateArray().Select(p => p.GetString())
			.Should().Equal(["fantasma-familia:read"], "a instância da família nunca é órfã para o SecureGate");
	}

	[Fact]
	public async Task RemoveUnknown_KeepsFamilyInstances_AndRemovesTrueOrphans()
	{
		var prefix = NewPrefix();
		(await PublishAsync(NewProduct(), ($"{prefix}-{{id}}:read", "Ler"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		await IdentitySeed.RoleAsync(factory, _tenantId, "limpeza-familia", $"{prefix}-rh:read", $"{prefix}-rh:write");

		var response = await _admin.PostAsync($"/api/v1/tenants/{_tenantId}/roles/remove-unknown-permissions", content: null);
		var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		body.GetProperty("roles")[0].GetProperty("removed").EnumerateArray().Select(p => p.GetString())
			.Should().Equal($"{prefix}-rh:write");
	}

	[Fact]
	public async Task Family_StopsBeingPublished_InstancesBecomeOrphansAgain()
	{
		var product = NewProduct();
		var prefix = NewPrefix();
		(await PublishAsync(product, ($"{prefix}-{{id}}:read", "Ler"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		await IdentitySeed.RoleAsync(factory, _tenantId, "familia-saiu", $"{prefix}-rh:read");

		(await PublishAsync(product)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		var role = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantId}/roles/familia-saiu", Json);

		role.GetProperty("unknownPermissions").EnumerateArray().Select(p => p.GetString())
			.Should().Equal($"{prefix}-rh:read");
	}
}
