using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Origem da atribuição de perfil (issue #28, ADR-0036). Nenhuma reconciliação automática existe
/// nesta entrega — a atribuição de origem <c>Directory</c> é simulada direto no banco, exatamente
/// como o trabalho futuro de sincronização a criaria.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class RoleAssignmentOriginTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private static string UniqueEmail() => $"origem-{Guid.NewGuid():N}@secco.test";

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
			new { name = "Tenant de origem", slug = $"t-{Guid.NewGuid():N}" });
		response.StatusCode.Should().Be(HttpStatusCode.Created);

		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetGuid();
	}

	/// <summary>Marca a atribuição já existente como vinda da sincronização de grupo (simulação).</summary>
	private async Task<Guid> MarkAsDirectoryOriginAsync(Guid tenantId, Guid userId, string roleName)
	{
		var sourceGroupId = Guid.NewGuid();
		var normalized = roleName.ToUpperInvariant();

		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var role = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == normalized);
		var assignment = await context.UserRoles.FirstAsync(ur => ur.UserId == userId && ur.RoleId == role.Id);
		assignment.Origin = Secco.SecureGate.Application.Users.RoleAssignmentOrigin.Directory;
		assignment.SourceGroupId = sourceGroupId;
		await context.SaveChangesAsync();

		return sourceGroupId;
	}

	[Fact]
	public async Task RemoveUserRole_AtribuicaoManual_RemoveNormalmente()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		(await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name = "leitor" }))
			.StatusCode.Should().Be(HttpStatusCode.Created);
		var userId = await IdentitySeed.UserAsync(factory, tenantId, UniqueEmail(), "leitor");

		var response = await admin.DeleteAsync($"/api/v1/tenants/{tenantId}/users/{userId}/roles/leitor");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}

	[Fact]
	public async Task RemoveUserRole_AtribuicaoDeDiretorio_Recusa()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		(await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name = "leitor" }))
			.StatusCode.Should().Be(HttpStatusCode.Created);
		var userId = await IdentitySeed.UserAsync(factory, tenantId, UniqueEmail(), "leitor");
		await MarkAsDirectoryOriginAsync(tenantId, userId, "leitor");

		var response = await admin.DeleteAsync($"/api/v1/tenants/{tenantId}/users/{userId}/roles/leitor");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await response.Content.ReadAsStringAsync()).Should().Contain("sincronização de grupo");

		// A remoção recusada não pode ter mexido em nada.
		var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/users/{userId}", Json);
		detail.GetProperty("roles").EnumerateArray().Should().ContainSingle(r => r.GetString() == "leitor");
	}

	[Fact]
	public async Task GetUser_MostraAOrigemDeCadaAtribuicao()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		(await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name = "leitor" }))
			.StatusCode.Should().Be(HttpStatusCode.Created);
		var userId = await IdentitySeed.UserAsync(factory, tenantId, UniqueEmail(), "leitor");
		var groupId = await MarkAsDirectoryOriginAsync(tenantId, userId, "leitor");

		var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/users/{userId}", Json);

		var assignment = detail.GetProperty("roleAssignments").EnumerateArray().Single(a => a.GetProperty("name").GetString() == "leitor");
		assignment.GetProperty("origin").GetString().Should().Be("Directory");
		assignment.GetProperty("sourceGroupId").GetGuid().Should().Be(groupId);
	}

	[Fact]
	public async Task ListRoleMembers_MostraAOrigemDeCadaMembro()
	{
		var admin = AdminClient();
		var tenantId = await TenantAsync(admin);
		(await admin.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name = "leitor" }))
			.StatusCode.Should().Be(HttpStatusCode.Created);
		var userId = await IdentitySeed.UserAsync(factory, tenantId, UniqueEmail(), "leitor");
		await MarkAsDirectoryOriginAsync(tenantId, userId, "leitor");

		var members = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles/leitor/members", Json);

		members.GetProperty("items").EnumerateArray().Should().ContainSingle()
			.Which.GetProperty("origin").GetString().Should().Be("Directory");
	}
}
