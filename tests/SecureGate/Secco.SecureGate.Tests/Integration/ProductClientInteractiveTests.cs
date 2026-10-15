using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Client interativo (<c>authorization_code</c>) vinculado a tenant pela API (ADR-0040): validação de
/// formato, tipo imutável, recusa no tenant de plataforma e o login de ponta a ponta emitindo o tenant
/// do vínculo. O ambiente de teste não é Development, então HTTPS é exigido.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ProductClientInteractiveTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private const string RedirectUri = "https://app.testes.local/callback";
	private const string Password = "Inter@tivo-Secco1!";
	private const string Scope = "openid profile email roles offline_access logstream";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;
	private HttpClient _admin = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
		_admin = IdentitySeed.AdminClient(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string ClientsOf(Guid tenantId) => $"/api/v1/tenants/{tenantId}/clients";

	private Task<HttpResponseMessage> PostInteractiveAsync(
		Guid tenantId, string? name = null, string[]? redirectUris = null, string[]? postLogout = null,
		string[]? roles = null, string[]? scopes = null) =>
		_admin.PostAsJsonAsync(ClientsOf(tenantId), new
		{
			name = name ?? $"Interativo {Guid.NewGuid():N}",
			scopes = scopes ?? ["logstream"],
			roles = roles ?? [],
			type = "authorization_code",
			redirectUris = redirectUris ?? [RedirectUri],
			postLogoutRedirectUris = postLogout ?? [],
		});

	private async Task<JsonElement> CreateInteractiveAsync(string[]? postLogout = null)
	{
		var response = await PostInteractiveAsync(_tenantId, postLogout: postLogout);
		response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());

		return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
	}

	[Fact]
	public async Task Create_Interativo_DevolveTipoUrisESecretUmaVez()
	{
		var created = await CreateInteractiveAsync(postLogout: ["https://app.testes.local/signed-out"]);
		var clientId = created.GetProperty("clientId").GetString()!;

		created.GetProperty("type").GetString().Should().Be("authorization_code");
		created.GetProperty("clientSecret").GetString().Should().NotBeNullOrWhiteSpace();
		created.GetProperty("redirectUris").EnumerateArray().Select(u => u.GetString()).Should().Equal(RedirectUri);
		created.GetProperty("postLogoutRedirectUris").EnumerateArray().Select(u => u.GetString())
			.Should().Equal("https://app.testes.local/signed-out");
		created.GetProperty("scopes").EnumerateArray().Select(s => s.GetString())
			.Should().Equal(["logstream"], "os escopos OIDC padrão são implícitos, não voltam na lista");

		var read = await _admin.GetStringAsync($"{ClientsOf(_tenantId)}/{clientId}");
		read.Should().NotContain("clientSecret");
		read.Should().Contain("authorization_code");
	}

	[Fact]
	public async Task Create_ClientCredentials_SegueDevolvendoTipoPadrao()
	{
		var response = await _admin.PostAsJsonAsync(
			ClientsOf(_tenantId),
			new { name = $"Maquina {Guid.NewGuid():N}", scopes = new[] { "logstream" }, roles = Array.Empty<string>() });

		response.StatusCode.Should().Be(HttpStatusCode.Created);
		var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
		created.GetProperty("type").GetString().Should().Be("client_credentials");
		created.TryGetProperty("redirectUris", out var uris).Should().BeTrue();
		uris.ValueKind.Should().Be(JsonValueKind.Null, "client de máquina não tem redirect URI");
	}

	[Theory]
	[InlineData("http://app.testes.local/callback")]       // http fora de Development
	[InlineData("/callback")]                              // relativa
	[InlineData("https://app.testes.local/cb#frag")]       // fragmento
	[InlineData("javascript:alert(1)")]                    // esquema que não é http(s)
	[InlineData("https://user:senha@app.testes.local/cb")] // credencial embutida
	[InlineData("")]
	public async Task Create_RedirectUriForaDaRegra_400(string uri)
	{
		var response = await PostInteractiveAsync(_tenantId, redirectUris: [uri]);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Create_RedirectUrisAusentesEmExcessoOuRepetidas_400()
	{
		(await PostInteractiveAsync(_tenantId, redirectUris: [])).StatusCode.Should().Be(HttpStatusCode.BadRequest);

		var many = Enumerable.Range(0, 11).Select(i => $"https://app.testes.local/cb{i}").ToArray();
		(await PostInteractiveAsync(_tenantId, redirectUris: many)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

		(await PostInteractiveAsync(_tenantId, redirectUris: [RedirectUri, RedirectUri]))
			.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Create_PostLogoutHttpForaDeDevelopment_400()
	{
		var response = await PostInteractiveAsync(_tenantId, postLogout: ["http://app.testes.local/out"]);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Create_InterativoComPapeis_400()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "papel-de-maquina", "log-entries:write");

		var response = await PostInteractiveAsync(_tenantId, roles: ["papel-de-maquina"]);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Create_ClientCredentialsComRedirectUris_400()
	{
		var response = await _admin.PostAsJsonAsync(ClientsOf(_tenantId), new
		{
			name = $"Maquina {Guid.NewGuid():N}",
			scopes = new[] { "logstream" },
			roles = Array.Empty<string>(),
			redirectUris = new[] { RedirectUri },
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Create_TipoDesconhecido_400()
	{
		var response = await _admin.PostAsJsonAsync(ClientsOf(_tenantId), new
		{
			name = $"Estranho {Guid.NewGuid():N}",
			scopes = new[] { "logstream" },
			roles = Array.Empty<string>(),
			type = "implicit",
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Theory]
	[InlineData("securegate:admin")]
	[InlineData("authorization:read")]
	[InlineData("catalog:logstream")]
	public async Task Create_InterativoComEscopoDeInfraestrutura_400(string scope)
	{
		var response = await PostInteractiveAsync(_tenantId, scopes: [scope]);

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Create_InterativoNoTenantDePlataforma_409()
	{
		await factory.EnsureDatabaseMigratedAsync();

		var response = await PostInteractiveAsync(SecureGatePlatform.TenantId);

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await response.Content.ReadAsStringAsync()).Should().Contain("InteractiveInPlatformTenant");
	}

	[Fact]
	public async Task Update_MudarOTipo_400NosDoisSentidos()
	{
		var interactive = (await CreateInteractiveAsync()).GetProperty("clientId").GetString()!;
		var machine = (await (await _admin.PostAsJsonAsync(ClientsOf(_tenantId), new
		{
			name = $"Maquina {Guid.NewGuid():N}",
			scopes = new[] { "logstream" },
			roles = Array.Empty<string>(),
		})).Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("clientId").GetString()!;

		// interativo → máquina (tipo omitido vale client_credentials)
		var toMachine = await _admin.PutAsJsonAsync(
			$"{ClientsOf(_tenantId)}/{interactive}",
			new { name = "Renomeado", scopes = new[] { "logstream" }, roles = Array.Empty<string>() });
		toMachine.StatusCode.Should().Be(HttpStatusCode.BadRequest);

		// máquina → interativo
		var toInteractive = await _admin.PutAsJsonAsync(
			$"{ClientsOf(_tenantId)}/{machine}",
			new
			{
				name = "Renomeado",
				scopes = new[] { "logstream" },
				roles = Array.Empty<string>(),
				type = "authorization_code",
				redirectUris = new[] { RedirectUri },
			});
		toInteractive.StatusCode.Should().Be(HttpStatusCode.BadRequest);

		// nada mudou: o interativo segue interativo, a máquina segue máquina
		(await _admin.GetFromJsonAsync<JsonElement>($"{ClientsOf(_tenantId)}/{interactive}", Json))
			.GetProperty("type").GetString().Should().Be("authorization_code");
		(await _admin.GetFromJsonAsync<JsonElement>($"{ClientsOf(_tenantId)}/{machine}", Json))
			.GetProperty("type").GetString().Should().Be("client_credentials");
	}

	[Fact]
	public async Task Update_Interativo_TrocaUrisDeFormaIdempotenteEMantemSecret()
	{
		var created = await CreateInteractiveAsync();
		var clientId = created.GetProperty("clientId").GetString()!;
		var secret = created.GetProperty("clientSecret").GetString()!;
		var body = new
		{
			name = $"Novo nome {Guid.NewGuid():N}",
			scopes = new[] { "logstream" },
			roles = Array.Empty<string>(),
			type = "authorization_code",
			redirectUris = new[] { "https://outro.testes.local/callback" },
			postLogoutRedirectUris = new[] { "https://outro.testes.local/out" },
		};

		(await _admin.PutAsJsonAsync($"{ClientsOf(_tenantId)}/{clientId}", body)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await _admin.PutAsJsonAsync($"{ClientsOf(_tenantId)}/{clientId}", body)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var read = await _admin.GetFromJsonAsync<JsonElement>($"{ClientsOf(_tenantId)}/{clientId}", Json);
		read.GetProperty("redirectUris").EnumerateArray().Select(u => u.GetString())
			.Should().Equal("https://outro.testes.local/callback");
		read.GetProperty("postLogoutRedirectUris").EnumerateArray().Select(u => u.GetString())
			.Should().Equal("https://outro.testes.local/out");
		read.GetProperty("type").GetString().Should().Be("authorization_code");

		// O secret não foi tocado pela alteração: a troca do code ainda o aceita (feito no E2E); aqui
		// basta provar que o client não ganhou grant de máquina
		var machineToken = await factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(
			new Dictionary<string, string>
			{
				["grant_type"] = "client_credentials",
				["client_id"] = clientId,
				["client_secret"] = secret,
				["scope"] = "logstream",
			}));
		machineToken.StatusCode.Should().Be(HttpStatusCode.BadRequest, "client interativo não tem o grant client_credentials");
	}
}
