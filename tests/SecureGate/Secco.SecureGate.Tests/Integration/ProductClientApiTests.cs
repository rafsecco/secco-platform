using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Gestão de client de produto pela API (ADR-0037).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ProductClientApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;
	private HttpClient _admin = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, _tenantId, "compras-writer", "log-entries:write");
		_admin = IdentitySeed.AdminClient(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private string Clients => $"/api/v1/tenants/{_tenantId}/clients";

	private async Task<JsonElement> CreateAsync(string name, string[]? scopes = null, string[]? roles = null)
	{
		var response = await _admin.PostAsJsonAsync(Clients, new { name, scopes = scopes ?? ["logstream"], roles = roles ?? [] });
		response.StatusCode.Should().Be(HttpStatusCode.Created);
		return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
	}

	private Task<HttpResponseMessage> TokenAsync(string clientId, string secret) =>
		factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = secret,
			["scope"] = "logstream",
		}));

	[Fact]
	public async Task Create_Valido_SecretFuncionaETokenSaiNoTenant()
	{
		var created = await CreateAsync("Compras", roles: ["COMPRAS-WRITER"]);
		var clientId = created.GetProperty("clientId").GetString()!;

		created.GetProperty("roles").EnumerateArray().Select(r => r.GetString())
			.Should().Equal(["compras-writer"], "grava o nome canônico do tb_roles");

		var token = await TokenAsync(clientId, created.GetProperty("clientSecret").GetString()!);
		token.StatusCode.Should().Be(HttpStatusCode.OK);
		var jwt = new JsonWebTokenHandler().ReadJsonWebToken(
			(await token.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString());
		jwt.GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString());
	}

	[Fact]
	public async Task GetEList_NuncaDevolvemSecret()
	{
		var created = await CreateAsync($"Sem segredo {Guid.NewGuid():N}");
		var clientId = created.GetProperty("clientId").GetString()!;

		(await _admin.GetStringAsync($"{Clients}/{clientId}")).Should().NotContain("clientSecret");
		(await _admin.GetStringAsync(Clients)).Should().NotContain("clientSecret");
	}

	[Theory]
	[InlineData("securegate:admin")]
	[InlineData("authorization:read")]
	[InlineData("catalog:logstream")]
	[InlineData("securegate")]
	public async Task Create_EscopoDeInfraestrutura_400(string scope)
	{
		var response = await _admin.PostAsJsonAsync(Clients, new { name = "x", scopes = new[] { scope }, roles = Array.Empty<string>() });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Create_NomeDuplicado_409()
	{
		var name = $"Duplicado {Guid.NewGuid():N}";
		await CreateAsync(name);

		var response = await _admin.PostAsJsonAsync(Clients, new { name, scopes = new[] { "logstream" }, roles = Array.Empty<string>() });

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Update_RepetidoComMesmoNome_204DuasVezesEEstadoIgual()
	{
		var name = $"Repetido {Guid.NewGuid():N}";
		var clientId = (await CreateAsync(name)).GetProperty("clientId").GetString()!;
		var body = new { name, scopes = new[] { "logstream", "notificationhub" }, roles = new[] { "compras-writer" } };

		(await _admin.PutAsJsonAsync($"{Clients}/{clientId}", body)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await _admin.PutAsJsonAsync($"{Clients}/{clientId}", body)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var client = await _admin.GetFromJsonAsync<JsonElement>($"{Clients}/{clientId}", Json);
		client.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).Should().Equal("logstream", "notificationhub");
		client.GetProperty("roles").EnumerateArray().Select(s => s.GetString()).Should().Equal("compras-writer");
	}

	[Fact]
	public async Task Update_NaoTrocaOSecret()
	{
		var created = await CreateAsync($"Mantem segredo {Guid.NewGuid():N}");
		var clientId = created.GetProperty("clientId").GetString()!;
		var secret = created.GetProperty("clientSecret").GetString()!;

		var body = new { name = $"Renomeado {Guid.NewGuid():N}", scopes = new[] { "logstream" }, roles = Array.Empty<string>() };
		(await _admin.PutAsJsonAsync($"{Clients}/{clientId}", body)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await TokenAsync(clientId, secret)).StatusCode.Should().Be(HttpStatusCode.OK, "alterar acesso não troca a credencial");
	}

	[Fact]
	public async Task Rotate_SecretAntigoMorreNovoFunciona()
	{
		var created = await CreateAsync($"Rotacao {Guid.NewGuid():N}");
		var clientId = created.GetProperty("clientId").GetString()!;
		var oldSecret = created.GetProperty("clientSecret").GetString()!;

		var rotated = await (await _admin.PostAsync($"{Clients}/{clientId}/rotate-secret", null))
			.Content.ReadFromJsonAsync<JsonElement>(Json);
		var newSecret = rotated.GetProperty("clientSecret").GetString()!;

		newSecret.Should().NotBe(oldSecret);
		(await TokenAsync(clientId, oldSecret)).IsSuccessStatusCode.Should().BeFalse();
		(await TokenAsync(clientId, newSecret)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Delete_RepetidoResponde404ESemToken()
	{
		var created = await CreateAsync($"Revogado {Guid.NewGuid():N}");
		var clientId = created.GetProperty("clientId").GetString()!;

		(await _admin.DeleteAsync($"{Clients}/{clientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await _admin.DeleteAsync($"{Clients}/{clientId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await TokenAsync(clientId, created.GetProperty("clientSecret").GetString()!)).IsSuccessStatusCode.Should().BeFalse();
	}

	[Fact]
	public async Task ClientDeOutroTenant_404EmTodasAsRotas()
	{
		var clientId = (await CreateAsync($"Alheio {Guid.NewGuid():N}")).GetProperty("clientId").GetString()!;
		var other = $"/api/v1/tenants/{await IdentitySeed.TenantAsync(factory)}/clients/{clientId}";

		await AssertNotFoundEverywhereAsync(other);
	}

	[Fact]
	public async Task ClientDePlataforma_InvisivelPelaApi()
	{
		var platformId = $"plataforma-{Guid.NewGuid():N}"[..30];
		await factory.CreateClientAsync(platformId, "plataforma-secret-de-32-chars-min!!!", "logstream");

		await AssertNotFoundEverywhereAsync($"{Clients}/{platformId}");
		(await _admin.GetStringAsync(Clients)).Should().NotContain(platformId);
	}

	[Fact]
	public async Task SemEscopoAdmin_403()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateTokenWithScopes("logstream"));

		(await client.GetAsync(Clients)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	private async Task AssertNotFoundEverywhereAsync(string route)
	{
		var body = new { name = "x", scopes = new[] { "logstream" }, roles = Array.Empty<string>() };

		(await _admin.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await _admin.PutAsJsonAsync(route, body)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await _admin.PostAsync($"{route}/rotate-secret", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await _admin.DeleteAsync(route)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
