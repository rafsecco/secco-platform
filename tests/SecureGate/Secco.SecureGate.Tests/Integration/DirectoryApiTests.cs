using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Diretório de pessoas do tenant para client de produto (ADR-0045): o tenant sai SÓ da claim do
/// token, o contrato carrega identidade, atributos do diretório e nada de segurança.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class DirectoryApiTests(SelfIssuedAuthSecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private const string AdminClientId = "directory-admin";
	private const string AdminSecret = "directory-admin-secret-32-chars-minimo!";

	private Guid _tenantId;
	private Guid _otherTenantId;
	private HttpClient _admin = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
		_otherTenantId = await IdentitySeed.TenantAsync(factory);

		// O SecureGate valida os próprios tokens aqui: o admin é um client de plataforma de verdade
		await factory.CreateClientAsync(AdminClientId, AdminSecret, SecureGateScopes.Admin);
		_admin = factory.CreateClient();
		_admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
			"Bearer", await TokenAsync(AdminClientId, AdminSecret, SecureGateScopes.Admin));
	}

	private async Task<string> TokenAsync(string clientId, string secret, string scope)
	{
		var response = await factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = secret,
			["scope"] = scope,
		}));
		response.StatusCode.Should().Be(HttpStatusCode.OK);

		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString()!;
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static readonly string[] Fields =
	[
		"id", "email", "displayName", "isActive", "isOwner",
		"jobTitle", "department", "businessPhone", "managerId", "attributesSource",
	];

	private static readonly string[] AttributeFields = ["jobTitle", "department", "businessPhone", "managerId", "attributesSource"];

	private static readonly string[] SecurityFields =
		["twoFactorEnabled", "externalLogins", "roles", "lockoutEnd", "lockoutEnabled", "lockoutReason", "securityStamp"];

	private async Task SetAttributesAsync(Guid userId, string? jobTitle, string? department, string? phone, Guid? managerId, DirectoryAttributesOrigin origin)
	{
		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var user = await context.Users.SingleAsync(u => u.Id == userId);
		user.JobTitle = jobTitle;
		user.Department = department;
		user.BusinessPhone = phone;
		user.ManagerId = managerId;
		user.AttributesOrigin = origin;
		await context.SaveChangesAsync();
	}

	private static string Email() => $"pessoa-{Guid.NewGuid():N}@secco.test";

	/// <summary>Cria, pela API, um client de produto do tenant com os escopos dados e devolve um cliente HTTP já com o token dele.</summary>
	private async Task<HttpClient> ProductClientAsync(Guid tenantId, params string[] scopes)
	{
		var created = await _admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/clients", new { name = $"Diretorio {Guid.NewGuid():N}", scopes, roles = Array.Empty<string>() });
		created.StatusCode.Should().Be(HttpStatusCode.Created);
		var body = await created.Content.ReadFromJsonAsync<JsonElement>(Json);

		var token = await TokenAsync(
			body.GetProperty("clientId").GetString()!, body.GetProperty("clientSecret").GetString()!, string.Join(' ', scopes));

		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

		return client;
	}

	private async Task AddMemberAsync(Guid tenantId, string email) =>
		(await _admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/members", new { email }, Json))
			.StatusCode.Should().Be(HttpStatusCode.Created);

	private async Task<JsonElement> ListAsync(HttpClient client, string query = "")
	{
		var response = await client.GetAsync("/api/v1/directory/people" + query);
		response.StatusCode.Should().Be(HttpStatusCode.OK);
		return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
	}

	private static string[] Emails(JsonElement page) =>
		[.. page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("email").GetString()!)];

	[Fact]
	public async Task List_ClientComEscopo_ListaDonosEMembrosAtivosDoProprioTenant()
	{
		var ownerEmail = Email();
		var memberEmail = Email();
		var suspendedEmail = Email();
		var deactivatedEmail = Email();
		var foreignEmail = Email();
		var memberId = await IdentitySeed.UserAsync(factory, _otherTenantId, memberEmail);
		var suspendedId = await IdentitySeed.UserAsync(factory, _otherTenantId, suspendedEmail);
		var ownerId = await IdentitySeed.UserAsync(factory, _tenantId, ownerEmail);
		var deactivatedId = await IdentitySeed.UserAsync(factory, _tenantId, deactivatedEmail);
		await IdentitySeed.UserAsync(factory, _otherTenantId, foreignEmail);

		await AddMemberAsync(_tenantId, memberEmail);
		await AddMemberAsync(_tenantId, suspendedEmail);
		(await _admin.PostAsync($"/api/v1/tenants/{_tenantId}/members/{suspendedId}/suspend", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await _admin.PostAsync($"/api/v1/tenants/{_tenantId}/users/{deactivatedId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);
		var page = await ListAsync(client);

		var people = page.GetProperty("items").EnumerateArray().ToDictionary(p => p.GetProperty("email").GetString()!);
		people.Keys.Should().BeEquivalentTo([ownerEmail, memberEmail, deactivatedEmail]);
		people.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal, "ordenado por e-mail");
		people[ownerEmail].GetProperty("id").GetGuid().Should().Be(ownerId);
		people[ownerEmail].GetProperty("isOwner").GetBoolean().Should().BeTrue();
		people[ownerEmail].GetProperty("isActive").GetBoolean().Should().BeTrue();
		people[memberEmail].GetProperty("id").GetGuid().Should().Be(memberId);
		people[memberEmail].GetProperty("isOwner").GetBoolean().Should().BeFalse();
		people[deactivatedEmail].GetProperty("isActive").GetBoolean().Should().BeFalse();
		page.GetProperty("totalCount").GetInt64().Should().Be(3);
	}

	[Fact]
	public async Task List_RespostaTemSoOsCamposDoContrato()
	{
		await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		var page = await ListAsync(client);

		page.GetProperty("items").EnumerateArray().Should().NotBeEmpty();
		foreach (var person in page.GetProperty("items").EnumerateArray())
		{
			person.EnumerateObject().Select(p => p.Name).Should()
				.BeEquivalentTo(Fields);
			person.EnumerateObject().Select(p => p.Name).Should().NotIntersectWith(SecurityFields);
		}
	}

	[Fact]
	public async Task List_TamanhoAcimaDeCem_ReduzidoACem()
	{
		await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		(await ListAsync(client, "?size=500")).GetProperty("size").GetInt32().Should().Be(100);
		(await ListAsync(client, "?size=7")).GetProperty("size").GetInt32().Should().Be(7);
	}

	[Fact]
	public async Task Get_PessoaDoTenant_Retorna200ComOContratoMinimo()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, email);
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		var response = await client.GetAsync($"/api/v1/directory/people/{userId}");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var person = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
		person.GetProperty("email").GetString().Should().Be(email);
		person.EnumerateObject().Select(p => p.Name).Should()
			.BeEquivalentTo(Fields);
	}

	[Fact]
	public async Task Get_PessoaDeOutroTenantOuInexistente_Retorna404()
	{
		var foreignId = await IdentitySeed.UserAsync(factory, _otherTenantId, Email());
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		(await client.GetAsync($"/api/v1/directory/people/{foreignId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await client.GetAsync($"/api/v1/directory/people/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Get_MembroSuspenso_Retorna404()
	{
		var email = Email();
		var userId = await IdentitySeed.UserAsync(factory, _otherTenantId, email);
		await AddMemberAsync(_tenantId, email);
		await _admin.PostAsync($"/api/v1/tenants/{_tenantId}/members/{userId}/suspend", null);
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		(await client.GetAsync($"/api/v1/directory/people/{userId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task List_ParametroDeTenantNaQuery_NaoMudaOTenant()
	{
		var foreignEmail = Email();
		await IdentitySeed.UserAsync(factory, _otherTenantId, foreignEmail);
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		var page = await ListAsync(client, $"?tenantId={_otherTenantId}");

		Emails(page).Should().NotContain(foreignEmail);

		// Header de outro tenant em conflito com a claim: a regra do SDK (ADR-0005) recusa, e nada vaza
		client.DefaultRequestHeaders.Add("X-Tenant-Id", _otherTenantId.ToString());
		var conflicting = await client.GetAsync("/api/v1/directory/people");
		conflicting.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await conflicting.Content.ReadAsStringAsync()).Should().NotContain(foreignEmail);
	}

	[Fact]
	public async Task List_TokenDeOutroEscopoDoSecureGate_Retorna403()
	{
		const string readerId = "directory-reader-sem-escopo";
		const string readerSecret = "directory-reader-secret-32-chars-min!!";
		await factory.CreateClientAsync(readerId, readerSecret, SecureGateScopes.AuthorizationRead);
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
			"Bearer", await TokenAsync(readerId, readerSecret, SecureGateScopes.AuthorizationRead));

		(await client.GetAsync("/api/v1/directory/people")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await client.GetAsync($"/api/v1/directory/people/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task List_TokenDeProdutoSemOEscopoDeDiretorio_NaoPassa()
	{
		// O token de "logstream" nem tem o SecureGate como audiência: barrado antes da autorização
		var client = await ProductClientAsync(_tenantId, "logstream");

		(await client.GetAsync("/api/v1/directory/people")).StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task List_TokenAdminSemTenantId_Retorna403()
	{
		// Platform client sem tenant_id: o scope de diretório nem é concedível a ele, então o token só leva admin
		(await _admin.GetAsync("/api/v1/directory/people")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task List_SemToken_Retorna401()
	{
		(await factory.CreateClient().GetAsync("/api/v1/directory/people")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task CriarClientInterativoComEscopoDeDiretorio_Retorna400()
	{
		var response = await _admin.PostAsJsonAsync($"/api/v1/tenants/{_tenantId}/clients", new
		{
			name = $"Interativo {Guid.NewGuid():N}",
			type = "authorization_code",
			scopes = new[] { SecureGateScopes.DirectoryRead },
			redirectUris = new[] { "https://app.exemplo.com/cb" },
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Diretorio_ContaComAtributos_ExpoeCamposEOrigem()
	{
		var managerId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await SetAttributesAsync(userId, "Analista", "Financeiro", "+55 11 4000-0000", managerId, DirectoryAttributesOrigin.Directory);
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		var person = await (await client.GetAsync($"/api/v1/directory/people/{userId}")).Content.ReadFromJsonAsync<JsonElement>(Json);

		person.GetProperty("jobTitle").GetString().Should().Be("Analista");
		person.GetProperty("department").GetString().Should().Be("Financeiro");
		person.GetProperty("businessPhone").GetString().Should().Be("+55 11 4000-0000");
		person.GetProperty("managerId").GetGuid().Should().Be(managerId);
		person.GetProperty("attributesSource").GetString().Should().Be("directory");

		var listed = (await ListAsync(client)).GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == userId);
		listed.GetProperty("attributesSource").GetString().Should().Be("directory");
	}

	[Fact]
	public async Task Diretorio_ContaLocal_CamposNulosEOrigemNula()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		var person = await (await client.GetAsync($"/api/v1/directory/people/{userId}")).Content.ReadFromJsonAsync<JsonElement>(Json);

		foreach (var name in AttributeFields)
		{
			person.GetProperty(name).ValueKind.Should().Be(JsonValueKind.Null, name);
		}
	}

	[Fact]
	public async Task Diretorio_GestorGravadoDeOutroTenant_ContaDona_ManagerIdNulo()
	{
		// Defesa em profundidade: a sincronização só resolve gestor no próprio tenant, mas um ManagerId que aponte
		// para conta de outro tenant (dado corrompido, migração manual) não pode revelar essa conta
		var foreignManagerId = await IdentitySeed.UserAsync(factory, _otherTenantId, Email());
		var personId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await SetAttributesAsync(personId, "Analista", null, null, foreignManagerId, DirectoryAttributesOrigin.Directory);
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		var person = await (await client.GetAsync($"/api/v1/directory/people/{personId}")).Content.ReadFromJsonAsync<JsonElement>(Json);

		person.GetProperty("jobTitle").GetString().Should().Be("Analista");
		person.GetProperty("managerId").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Fact]
	public async Task Diretorio_GestorNaoVisivelNoTenant_ManagerIdNulo()
	{
		// Pessoa do tenant A (_otherTenantId), membro do B (_tenantId); o gestor só existe em A
		var personEmail = Email();
		var managerId = await IdentitySeed.UserAsync(factory, _otherTenantId, Email());
		var personId = await IdentitySeed.UserAsync(factory, _otherTenantId, personEmail);
		await SetAttributesAsync(personId, "Analista", null, null, managerId, DirectoryAttributesOrigin.Directory);
		await AddMemberAsync(_tenantId, personEmail);

		var clientB = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);
		var clientA = await ProductClientAsync(_otherTenantId, SecureGateScopes.DirectoryRead);

		var seenByB = await (await clientB.GetAsync($"/api/v1/directory/people/{personId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
		var seenByA = await (await clientA.GetAsync($"/api/v1/directory/people/{personId}")).Content.ReadFromJsonAsync<JsonElement>(Json);

		seenByB.GetProperty("managerId").ValueKind.Should().Be(JsonValueKind.Null);
		seenByA.GetProperty("managerId").GetGuid().Should().Be(managerId);

		var listedByB = (await ListAsync(clientB)).GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == personId);
		listedByB.GetProperty("managerId").ValueKind.Should().Be(JsonValueKind.Null);
	}

	[Fact]
	public async Task Diretorio_JsonDoAttributesSource_EhString()
	{
		var userId = await IdentitySeed.UserAsync(factory, _tenantId, Email());
		await SetAttributesAsync(userId, null, null, null, null, DirectoryAttributesOrigin.Directory);
		var client = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);

		var person = await (await client.GetAsync($"/api/v1/directory/people/{userId}")).Content.ReadFromJsonAsync<JsonElement>(Json);

		person.GetProperty("attributesSource").ValueKind.Should().Be(JsonValueKind.String);
	}

	[Fact]
	public async Task Diretorio_MembroDeOutroTenant_SemAtributosDoDiretorioDoDono()
	{
		// Atributos vêm do diretório do tenant dono (A); no tenant B, onde é só membro, não aparecem
		var personEmail = Email();
		var managerId = await IdentitySeed.UserAsync(factory, _otherTenantId, Email());
		var personId = await IdentitySeed.UserAsync(factory, _otherTenantId, personEmail);
		await SetAttributesAsync(personId, "Analista", "Financeiro", "+55 11 4000-0000", managerId, DirectoryAttributesOrigin.Directory);
		await AddMemberAsync(_tenantId, personEmail);

		var clientB = await ProductClientAsync(_tenantId, SecureGateScopes.DirectoryRead);
		var clientA = await ProductClientAsync(_otherTenantId, SecureGateScopes.DirectoryRead);

		var byId = await (await clientB.GetAsync($"/api/v1/directory/people/{personId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
		var listed = (await ListAsync(clientB)).GetProperty("items").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == personId);
		foreach (var seen in new[] { byId, listed })
		{
			seen.GetProperty("isOwner").GetBoolean().Should().BeFalse();
			foreach (var name in AttributeFields)
			{
				seen.GetProperty(name).ValueKind.Should().Be(JsonValueKind.Null, name);
			}
		}

		var owner = await (await clientA.GetAsync($"/api/v1/directory/people/{personId}")).Content.ReadFromJsonAsync<JsonElement>(Json);
		owner.GetProperty("jobTitle").GetString().Should().Be("Analista");
		owner.GetProperty("department").GetString().Should().Be("Financeiro");
		owner.GetProperty("businessPhone").GetString().Should().Be("+55 11 4000-0000");
		owner.GetProperty("managerId").GetGuid().Should().Be(managerId);
		owner.GetProperty("attributesSource").GetString().Should().Be("directory");
	}
}
