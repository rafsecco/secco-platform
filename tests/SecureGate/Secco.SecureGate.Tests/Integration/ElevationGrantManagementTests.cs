using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Gestão da concessão de elevação (ADR-0031): PUT idempotente/GET/DELETE gated pelo scope
/// <c>securegate:admin</c>, como <see cref="TenantFederationManagementTests"/>. O ponto crítico
/// de segurança (ADR-0020) é que um usuário de OUTRO tenant na rota responda 404 IDÊNTICO a
/// usuário inexistente — nunca 403, nunca mensagem diferente.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ElevationGrantManagementTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private const string ValidPassword = "Str0ng@Pass!";

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient CreateClientWithScopes(params string[] scopes)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(scopes));

		return client;
	}

	private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@secco.test";

	private async Task<Guid> CreateTenantAsync(HttpClient admin)
	{
		var response = await admin.PostAsJsonAsync("/api/v1/tenants",
			new { name = "Tenant de elevação", slug = $"t-{Guid.NewGuid():N}" });
		response.StatusCode.Should().Be(HttpStatusCode.Created);

		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
	}

	private async Task<Guid> CreateUserAsync(HttpClient admin, Guid tenantId)
	{
		var response = await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/users",
			new { email = UniqueEmail(), password = ValidPassword });
		response.StatusCode.Should().Be(HttpStatusCode.Created);

		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
	}

	private static string ElevationRoute(Guid tenantId, Guid userId) =>
		$"/api/v1/tenants/{tenantId}/users/{userId}/elevation";

	[Fact]
	public async Task GrantElevation_WithoutToken_Returns401()
	{
		var response = await factory.CreateClient()
			.PutAsJsonAsync(ElevationRoute(Guid.NewGuid(), Guid.NewGuid()), new { });

		response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task GrantElevation_WithoutAdminScope_Returns403()
	{
		var response = await CreateClientWithScopes("logstream")
			.PutAsJsonAsync(ElevationRoute(Guid.NewGuid(), Guid.NewGuid()), new { });

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task GrantElevation_ThenGetElevation_ReturnsActiveGrantWithoutExpiration()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantId = await CreateTenantAsync(admin);
		var userId = await CreateUserAsync(admin, tenantId);

		var grant = await admin.PutAsJsonAsync(ElevationRoute(tenantId, userId), new { });
		grant.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var fetched = await admin.GetFromJsonAsync<JsonElement>(ElevationRoute(tenantId, userId), Json);

		fetched.GetProperty("userId").GetGuid().Should().Be(userId);
		fetched.GetProperty("tenantId").GetGuid().Should().Be(tenantId);
		fetched.GetProperty("expiresAt").ValueKind.Should().Be(JsonValueKind.Null, "ExpiresAt ausente = sem expiração");
		fetched.GetProperty("isActive").GetBoolean().Should().BeTrue();
		fetched.TryGetProperty("grantedBy", out var grantedBy).Should().BeTrue();
		grantedBy.GetString().Should().NotBeNullOrEmpty("GrantedBy vem do claim 'sub' do chamador, nunca do corpo");
	}

	[Fact]
	public async Task GrantElevation_Twice_RenewsWithoutDuplicating()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantId = await CreateTenantAsync(admin);
		var userId = await CreateUserAsync(admin, tenantId);
		var newExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);

		(await admin.PutAsJsonAsync(ElevationRoute(tenantId, userId), new { }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var second = await admin.PutAsJsonAsync(ElevationRoute(tenantId, userId), new { expiresAt = newExpiresAt });
		second.StatusCode.Should().Be(HttpStatusCode.NoContent, "o PUT é idempotente (ADR-0031)");

		var fetched = await admin.GetFromJsonAsync<JsonElement>(ElevationRoute(tenantId, userId), Json);
		fetched.GetProperty("expiresAt").GetDateTimeOffset().Should().BeCloseTo(newExpiresAt, TimeSpan.FromSeconds(1));
	}

	[Fact]
	public async Task GetElevation_WithoutGrant_Returns404()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantId = await CreateTenantAsync(admin);
		var userId = await CreateUserAsync(admin, tenantId);

		var response = await admin.GetAsync(ElevationRoute(tenantId, userId));

		response.StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task GrantElevation_WithExpiresAtInThePast_Returns400()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantId = await CreateTenantAsync(admin);
		var userId = await CreateUserAsync(admin, tenantId);

		var response = await admin.PutAsJsonAsync(ElevationRoute(tenantId, userId),
			new { expiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task RevokeElevation_RemovesGrant_AndSubsequentGetReturns404()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantId = await CreateTenantAsync(admin);
		var userId = await CreateUserAsync(admin, tenantId);
		(await admin.PutAsJsonAsync(ElevationRoute(tenantId, userId), new { }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var revoke = await admin.DeleteAsync(ElevationRoute(tenantId, userId));
		revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);

		var afterRevoke = await admin.GetAsync(ElevationRoute(tenantId, userId));
		afterRevoke.StatusCode.Should().Be(HttpStatusCode.NotFound, "a próxima troca de token deve ser bloqueada imediatamente");
	}

	[Fact]
	public async Task RevokeElevation_WhenNoGrantExists_Returns204Idempotently()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantId = await CreateTenantAsync(admin);
		var userId = await CreateUserAsync(admin, tenantId);

		var response = await admin.DeleteAsync(ElevationRoute(tenantId, userId));

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task GrantElevation_ForUserOfAnotherTenant_Returns404IdenticalToUnknownUser()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantA = await CreateTenantAsync(admin);
		var userOfTenantA = await CreateUserAsync(admin, tenantA);
		var tenantB = await CreateTenantAsync(admin);

		var wrongTenantResponse = await admin.PutAsJsonAsync(ElevationRoute(tenantB, userOfTenantA), new { });
		var unknownUserResponse = await admin.PutAsJsonAsync(ElevationRoute(tenantB, Guid.NewGuid()), new { });

		wrongTenantResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
			"a rota do tenant B não pode revelar que o usuário existe em outro tenant");
		wrongTenantResponse.StatusCode.Should().Be(unknownUserResponse.StatusCode);

		var wrongTenantBody = await wrongTenantResponse.Content.ReadAsStringAsync();
		var unknownUserBody = await unknownUserResponse.Content.ReadAsStringAsync();
		wrongTenantBody.Should().Be(unknownUserBody, "as duas respostas têm de ser indistinguíveis (ADR-0020)");
	}

	[Fact]
	public async Task GetElevation_ForUserOfAnotherTenant_Returns404IdenticalToUnknownUser()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantA = await CreateTenantAsync(admin);
		var userOfTenantA = await CreateUserAsync(admin, tenantA);
		var tenantB = await CreateTenantAsync(admin);

		var wrongTenantResponse = await admin.GetAsync(ElevationRoute(tenantB, userOfTenantA));
		var unknownUserResponse = await admin.GetAsync(ElevationRoute(tenantB, Guid.NewGuid()));

		wrongTenantResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

		var wrongTenantBody = await wrongTenantResponse.Content.ReadAsStringAsync();
		var unknownUserBody = await unknownUserResponse.Content.ReadAsStringAsync();
		wrongTenantBody.Should().Be(unknownUserBody, "as duas respostas têm de ser indistinguíveis (ADR-0020)");
	}

	[Fact]
	public async Task RevokeElevation_ForUserOfAnotherTenant_Returns404IdenticalToUnknownUser()
	{
		var admin = CreateClientWithScopes(SecureGateScopes.Admin);
		var tenantA = await CreateTenantAsync(admin);
		var userOfTenantA = await CreateUserAsync(admin, tenantA);
		var tenantB = await CreateTenantAsync(admin);

		var wrongTenantResponse = await admin.DeleteAsync(ElevationRoute(tenantB, userOfTenantA));
		var unknownUserResponse = await admin.DeleteAsync(ElevationRoute(tenantB, Guid.NewGuid()));

		wrongTenantResponse.StatusCode.Should().Be(HttpStatusCode.NotFound,
			"revogar pela rota errada não pode silenciosamente 'funcionar' (204) nem revelar o tenant certo");

		var wrongTenantBody = await wrongTenantResponse.Content.ReadAsStringAsync();
		var unknownUserBody = await unknownUserResponse.Content.ReadAsStringAsync();
		wrongTenantBody.Should().Be(unknownUserBody, "as duas respostas têm de ser indistinguíveis (ADR-0020)");
	}
}
