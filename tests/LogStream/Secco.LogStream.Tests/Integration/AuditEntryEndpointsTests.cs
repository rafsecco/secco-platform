using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

public class AuditEntryEndpointsTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>, IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CreateClient(string role = LogStreamApiFactory.DefaultTestRole)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateToken(factory.TenantAlfa, role: role));
		return client;
	}

	[Fact]
	public async Task Post_WhenValid_Returns201AndPersistsBeforeResponding()
	{
		var client = CreateClient();
		var marker = Guid.NewGuid().ToString("N");

		var response = await client.PostAsJsonAsync("/api/v1/audit-entries", new
		{
			actorId = $"user-{marker}",
			actorType = "User",
			action = "documento.download",
			actorName = "Fulano de Tal",
			resourceType = "documento",
			resourceId = "doc-42",
			metadata = "{\"paginas\":3}",
		});

		response.StatusCode.Should().Be(HttpStatusCode.Created);

		var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
		var id = created.GetProperty("id").GetGuid();
		created.GetProperty("actorId").GetString().Should().Be($"user-{marker}");

		// A diferença síncrona é o ponto: sem espera nenhuma, o GET tem que encontrar o registro
		var persisted = await client.GetAsync($"/api/v1/audit-entries/{id}");
		persisted.StatusCode.Should().Be(HttpStatusCode.OK);

		var dto = await persisted.Content.ReadFromJsonAsync<JsonElement>(Json);
		dto.GetProperty("action").GetString().Should().Be("documento.download");
		dto.GetProperty("resourceId").GetString().Should().Be("doc-42");
	}

	[Fact]
	public async Task Post_WithoutWritePermission_Returns403()
	{
		var client = CreateClient(role: "role-sem-permissao-de-auditoria");

		var response = await client.PostAsJsonAsync("/api/v1/audit-entries", new
		{
			actorId = "user-x",
			actorType = "User",
			action = "documento.download",
		});

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task Post_WithoutActorId_Returns400()
	{
		var client = CreateClient();

		var response = await client.PostAsJsonAsync("/api/v1/audit-entries", new
		{
			actorId = "",
			actorType = "User",
			action = "documento.download",
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync()).Should().Contain("ActorIdRequired");
	}

	[Fact]
	public async Task Post_WithInvalidMetadataJson_Returns400()
	{
		var client = CreateClient();

		var response = await client.PostAsJsonAsync("/api/v1/audit-entries", new
		{
			actorId = "user-x",
			actorType = "User",
			action = "documento.download",
			metadata = "isto não é json",
		});

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync()).Should().Contain("MetadataInvalidJson");
	}

	[Fact]
	public async Task Search_WhenFilteredByActor_ReturnsOnlyThatActor()
	{
		var client = CreateClient();
		var marker = Guid.NewGuid().ToString("N");
		var targetActor = $"user-alvo-{marker}";

		await client.PostAsJsonAsync("/api/v1/audit-entries", new
		{
			actorId = targetActor,
			actorType = "User",
			action = "documento.download",
		});
		await client.PostAsJsonAsync("/api/v1/audit-entries", new
		{
			actorId = $"user-outro-{marker}",
			actorType = "User",
			action = "documento.download",
		});

		var filtered = await client.GetFromJsonAsync<JsonElement>(
			$"/api/v1/audit-entries?actorId={targetActor}", Json);

		filtered.GetProperty("totalCount").GetInt64().Should().Be(1);
		filtered.GetProperty("items")[0].GetProperty("actorId").GetString().Should().Be(targetActor);
	}

	[Fact]
	public async Task Get_UnknownId_Returns404()
	{
		var response = await CreateClient().GetAsync($"/api/v1/audit-entries/{Guid.NewGuid()}");

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
