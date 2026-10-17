using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Catálogo de permissões publicado por produto (ADR-0039): publicação sob <c>permissions:&lt;produto&gt;</c>
/// comparado com o produto da rota, leitura sob <c>securegate:admin</c>, republicação idempotente e
/// colisão de nome entre produtos visível sem bloquear. Produtos e recursos únicos por teste — o
/// catálogo é global e a suíte compartilha o banco.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class PermissionCatalogApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static string NewProduct() => $"p-{Guid.NewGuid():N}"[..24];

	private static string NewResource() => $"res-{Guid.NewGuid():N}"[..16];

	private HttpClient ClientWithScopes(params string[] scopes)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(scopes));

		return client;
	}

	private static object Body(params (string Name, string Description)[] permissions) =>
		new { permissions = permissions.Select(p => new { name = p.Name, description = p.Description }).ToArray() };

	private async Task<HttpResponseMessage> PublishAsync(string product, params (string Name, string Description)[] permissions)
	{
		using var client = ClientWithScopes(SecureGateScopes.PermissionsFor(product));

		return await client.PutAsJsonAsync($"/api/v1/permission-catalog/{product}", Body(permissions));
	}

	private async Task<JsonElement> ReadAsync(string product)
	{
		using var admin = ClientWithScopes(SecureGateScopes.Admin);

		return await admin.GetFromJsonAsync<JsonElement>($"/api/v1/permission-catalog/{product}", Json);
	}

	[Fact]
	public async Task Publish_WithOwnProductScope_Returns204_AndAdminReadsEntries()
	{
		var product = NewProduct();
		var resource = NewResource();

		var response = await PublishAsync(product, ($"{resource}:read", "Ler"), ($"{resource}:write", "Gravar"));
		var catalog = await ReadAsync(product);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
		catalog.GetProperty("product").GetString().Should().Be(product);
		catalog.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("name").GetString())
			.Should().Equal($"{resource}:read", $"{resource}:write");
		catalog.GetProperty("permissions")[0].GetProperty("description").GetString().Should().Be("Ler");
	}

	[Fact]
	public async Task Publish_SameCatalogTwice_KeepsPublishedAt()
	{
		var product = NewProduct();
		var resource = NewResource();
		(await PublishAsync(product, ($"{resource}:read", "Ler"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		var first = (await ReadAsync(product)).GetProperty("publishedAt").GetDateTimeOffset();
		await Task.Delay(50);

		var again = await PublishAsync(product, ($"{resource}:read", "Ler"));
		var second = (await ReadAsync(product)).GetProperty("publishedAt").GetDateTimeOffset();

		again.StatusCode.Should().Be(HttpStatusCode.NoContent);
		second.Should().Be(first, "republicar igual tem o mesmo efeito (ADR-0034)");
	}

	[Fact]
	public async Task Publish_ChangedDescription_UpdatesIt()
	{
		var product = NewProduct();
		var resource = NewResource();
		await PublishAsync(product, ($"{resource}:read", "Ler"));

		await PublishAsync(product, ($"{resource}:read", "Ler tudo"));
		var catalog = await ReadAsync(product);

		catalog.GetProperty("permissions")[0].GetProperty("description").GetString().Should().Be("Ler tudo");
	}

	[Fact]
	public async Task Publish_WithoutAnEntry_RemovesItFromCatalog()
	{
		var product = NewProduct();
		var resource = NewResource();
		await PublishAsync(product, ($"{resource}:read", "Ler"), ($"{resource}:write", "Gravar"));

		await PublishAsync(product, ($"{resource}:read", "Ler"));
		var catalog = await ReadAsync(product);

		catalog.GetProperty("permissions").EnumerateArray().Select(p => p.GetProperty("name").GetString())
			.Should().Equal($"{resource}:read");
	}

	[Fact]
	public async Task Publish_DoesNotTouchRoles()
	{
		var product = NewProduct();
		var permission = $"{NewResource()}:read";
		await PublishAsync(product, (permission, "Ler"));
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantId, "perfil", permission);

		await PublishAsync(product);   // retira a permissão do catálogo

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		context.RoleClaims.Count(c => c.ClaimValue == permission).Should().Be(1,
			"publicar nunca toca perfil (ADR-0039)");
	}

	[Fact]
	public async Task Publish_EmptyList_Returns204()
	{
		var product = NewProduct();
		(await PublishAsync(product, ("a-res:read", "Ler"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var response = await PublishAsync(product);

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task Publish_WithScopeOfAnotherProduct_Returns403()
	{
		var product = NewProduct();
		using var client = ClientWithScopes(SecureGateScopes.PermissionsFor("logstream"));

		var response = await client.PutAsJsonAsync($"/api/v1/permission-catalog/{product}", Body(("a:read", "Ler")));

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Publish_ProductRouteWithDifferentCase_Returns403_OrdinalComparison()
	{
		var product = NewProduct();
		using var client = ClientWithScopes(SecureGateScopes.PermissionsFor(product));

		var response = await client.PutAsJsonAsync($"/api/v1/permission-catalog/{product.ToUpperInvariant()}", Body(("a:read", "Ler")));

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Publish_WithoutScope_Returns403()
	{
		var product = NewProduct();
		using var client = ClientWithScopes(SecureGateScopes.AuthorizationRead, SecureGateScopes.CatalogFor(product));

		var response = await client.PutAsJsonAsync($"/api/v1/permission-catalog/{product}", Body(("a:read", "Ler")));

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Publish_WithAdminScopeOnly_Returns403()
	{
		var product = NewProduct();
		using var client = ClientWithScopes(SecureGateScopes.Admin);

		var response = await client.PutAsJsonAsync($"/api/v1/permission-catalog/{product}", Body(("a:read", "Ler")));

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "gerir o SecureGate não é publicar em nome do produto");
	}

	[Fact]
	public async Task Publish_InvalidName_Returns400()
	{
		var response = await PublishAsync(NewProduct(), ("Nome Ruim", "Ler"));

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Publish_WithoutPermissionsProperty_Returns400()
	{
		var product = NewProduct();
		using var client = ClientWithScopes(SecureGateScopes.PermissionsFor(product));

		var response = await client.PutAsJsonAsync($"/api/v1/permission-catalog/{product}", new { });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Read_WithoutAdminScope_Returns403()
	{
		var product = NewProduct();
		await PublishAsync(product, ("a-res:read", "Ler"));
		using var client = ClientWithScopes(SecureGateScopes.PermissionsFor(product));

		(await client.GetAsync($"/api/v1/permission-catalog/{product}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await client.GetAsync("/api/v1/permission-catalog")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Read_ProductThatNeverPublished_Returns404()
	{
		using var admin = ClientWithScopes(SecureGateScopes.Admin);

		var response = await admin.GetAsync($"/api/v1/permission-catalog/{NewProduct()}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Publish_SameNameInTwoProducts_BothSucceed_AndAlsoDeclaredByShowsTheOther()
	{
		var productA = NewProduct();
		var productB = NewProduct();
		var shared = $"{NewResource()}:read";

		var a = await PublishAsync(productA, (shared, "A"));
		var b = await PublishAsync(productB, (shared, "B"));
		var catalogA = await ReadAsync(productA);
		var catalogB = await ReadAsync(productB);

		a.StatusCode.Should().Be(HttpStatusCode.NoContent);
		b.StatusCode.Should().Be(HttpStatusCode.NoContent, "colisão é visível, não bloqueia (ADR-0039)");
		catalogA.GetProperty("permissions")[0].GetProperty("alsoDeclaredBy").EnumerateArray()
			.Select(e => e.GetString()).Should().Equal(productB);
		catalogB.GetProperty("permissions")[0].GetProperty("alsoDeclaredBy").EnumerateArray()
			.Select(e => e.GetString()).Should().Equal(productA);
	}

	[Fact]
	public async Task List_WithAdminScope_IncludesPublishedProducts()
	{
		var product = NewProduct();
		await PublishAsync(product, ("a-res:read", "Ler"));
		using var admin = ClientWithScopes(SecureGateScopes.Admin);

		var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/permission-catalog", Json);

		all.EnumerateArray().Select(p => p.GetProperty("product").GetString()).Should().Contain(product);
	}
}
