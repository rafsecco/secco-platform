using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Endpoint de leitura de grupos do diretório federado (issue #27, ADR-0036) — escopo exigido e o
/// caminho "sem federação" (nunca lista vazia). O caminho de sucesso contra o Graph de verdade não
/// é coberto aqui — exige um tenant Entra de teste (registrado na ADR-0036); o formato da chamada
/// ao Graph é coberto por <c>GraphGroupDirectoryTests</c> (Unit) contra um servidor falso.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class EntraGroupsEndpointTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

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
			new { name = "Tenant de grupos", slug = $"t-{Guid.NewGuid():N}" });
		response.StatusCode.Should().Be(HttpStatusCode.Created);

		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
	}

	[Fact]
	public async Task ListGroups_SemToken_Responde401()
	{
		var response = await factory.CreateClient().GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/entra/groups");

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task ListGroups_SemEscopoDeAdmin_Responde403()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes("logstream"));

		var response = await client.GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/entra/groups");

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ListGroups_TenantSemFederacao_RespondeErroClaroNuncaListaVazia()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);

		var response = await admin.GetAsync($"/api/v1/tenants/{tenantId}/entra/groups");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await response.Content.ReadAsStringAsync()).Should().Contain("federação");
	}

	[Fact]
	public async Task ListGroups_TenantComFederacaoDesabilitada_RespondeErroClaro()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);

		(await admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/federation",
			new { directoryId = Guid.NewGuid(), enabled = false }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var response = await admin.GetAsync($"/api/v1/tenants/{tenantId}/entra/groups");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task ListGroups_ComTermoDeBuscaComCaractereDeControle_Responde400()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);

		(await admin.PutAsJsonAsync($"/api/v1/tenants/{tenantId}/federation",
			new { directoryId = Guid.NewGuid(), enabled = true }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var response = await admin.GetAsync(
			$"/api/v1/tenants/{tenantId}/entra/groups?filter={Uri.EscapeDataString("a\r\nb")}");

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}
}
