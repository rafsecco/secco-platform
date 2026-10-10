using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Limpeza de permissões órfãs por tenant (ADR-0039): só o tenant da rota, nunca perfil reservado,
/// idempotente, e perfil sem órfã nem aparece no resumo.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class RemoveUnknownPermissionsTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private HttpClient _admin = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		await IdentitySeed.PermissionCatalogAsync(factory, "orphan-tests", "log-entries:read");
		_admin = IdentitySeed.AdminClient(factory);
	}

	public Task DisposeAsync()
	{
		_admin.Dispose();

		return Task.CompletedTask;
	}

	private Task<HttpResponseMessage> CleanAsync(Guid tenantId) =>
		_admin.PostAsync($"/api/v1/tenants/{tenantId}/roles/remove-unknown-permissions", content: null);

	private async Task<string[]> StoredPermissionsAsync(Guid tenantId, string role)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.RoleClaims
			.Where(c => c.ClaimType == "permission" && context.Roles.Any(r => r.Id == c.RoleId && r.TenantId == tenantId && r.Name == role))
			.Select(c => c.ClaimValue!)
			.OrderBy(v => v)
			.ToArrayAsync();
	}

	[Fact]
	public async Task Clean_RemovesOrphansOnlyFromTheRouteTenant()
	{
		var tenantA = await IdentitySeed.TenantAsync(factory);
		var tenantB = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantA, "leitor", "log-entries:read", "antiga:read", "removida:write");
		await IdentitySeed.RoleAsync(factory, tenantB, "leitor", "log-entries:read", "antiga:read");

		var response = await CleanAsync(tenantA);
		var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		body.GetProperty("rolesChanged").GetInt32().Should().Be(1);
		body.GetProperty("permissionsRemoved").GetInt32().Should().Be(2);
		body.GetProperty("roles")[0].GetProperty("role").GetString().Should().Be("leitor");
		body.GetProperty("roles")[0].GetProperty("removed").EnumerateArray().Select(p => p.GetString())
			.Should().Equal("antiga:read", "removida:write");
		(await StoredPermissionsAsync(tenantA, "leitor")).Should().Equal("log-entries:read");
		(await StoredPermissionsAsync(tenantB, "leitor")).Should().Equal("antiga:read", "log-entries:read");
	}

	[Fact]
	public async Task Clean_CalledTwice_SecondCallChangesNothing()
	{
		var tenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenant, "leitor", "antiga:read");
		(await CleanAsync(tenant)).StatusCode.Should().Be(HttpStatusCode.OK);

		var second = await (await CleanAsync(tenant)).Content.ReadFromJsonAsync<JsonElement>(Json);

		second.GetProperty("rolesChanged").GetInt32().Should().Be(0);
		second.GetProperty("permissionsRemoved").GetInt32().Should().Be(0);
	}

	[Fact]
	public async Task Clean_DoesNotTouchReservedRole()
	{
		var tenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenant, SecureGatePlatform.OperatorRole, "reservada-orfa:read");

		var body = await (await CleanAsync(tenant)).Content.ReadFromJsonAsync<JsonElement>(Json);

		body.GetProperty("rolesChanged").GetInt32().Should().Be(0);
		(await StoredPermissionsAsync(tenant, SecureGatePlatform.OperatorRole)).Should().Equal("reservada-orfa:read");
	}

	[Fact]
	public async Task Clean_RoleWithoutOrphans_DoesNotAppearInSummary()
	{
		var tenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenant, "limpo", "log-entries:read");
		await IdentitySeed.RoleAsync(factory, tenant, "sujo", "antiga:read");

		var body = await (await CleanAsync(tenant)).Content.ReadFromJsonAsync<JsonElement>(Json);

		body.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("role").GetString())
			.Should().Equal("sujo");
		(await StoredPermissionsAsync(tenant, "limpo")).Should().Equal("log-entries:read");
	}

	[Fact]
	public async Task Clean_UnknownTenant_Returns404()
	{
		(await CleanAsync(Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task Clean_WithoutAdminScope_Returns403()
	{
		using var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes("catalog:logstream"));

		var response = await client.PostAsync($"/api/v1/tenants/{Guid.NewGuid()}/roles/remove-unknown-permissions", content: null);

		response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}
}
