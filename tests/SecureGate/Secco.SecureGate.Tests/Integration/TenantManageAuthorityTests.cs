using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Gestão do próprio tenant por máquina (ADR-0052), ponta a ponta com token real: o client de produto com
/// <c>securegate:tenant-manage</c> alcança só as rotas da lista, só no tenant do próprio token, nunca uma conta
/// administrativa, e toda escrita passa antes pela trilha local do SecureGate (ADR-0054).
/// </summary>
[Collection(TenantAdminApiCollectionDefinition.Name)]
public class TenantManageAuthorityTests(TenantAdminSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string Secret = "tenant-manage-client-secret-32-chars!!";
	private const string OperatorClientId = "tenant-manage-operator-machine";
	private const string OperatorSecret = "tenant-manage-operator-machine-secret!";
	private const string Role = "setor-financeiro-user";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	// Cenário: a "Intranet" é o tenant A, com a máquina dela; B é outro sistema da empresa
	private Guid _tenantA;
	private Guid _tenantB;
	private Guid _person;
	private string _machineClientId = string.Empty;

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreateClientAsync(OperatorClientId, OperatorSecret, SecureGateScopes.Admin);
		secureGate.Auditor.Reset();
		secureGate.FailTenantManageAudit = false;

		_tenantA = await IdentitySeed.TenantAsync(secureGate);
		_tenantB = await IdentitySeed.TenantAsync(secureGate);
		await IdentitySeed.RoleAsync(secureGate, _tenantA, Role);
		_person = await IdentitySeed.UserAsync(secureGate, _tenantA, $"tm-{Guid.NewGuid():N}@secco.test");
		_machineClientId = await MachineClientAsync(_tenantA, SecureGateScopes.TenantManage);
	}

	public Task DisposeAsync()
	{
		secureGate.Auditor.Reset();
		secureGate.FailTenantManageAudit = false;

		return Task.CompletedTask;
	}

	// ---- O que a máquina pode, no próprio tenant ----

	[Fact]
	public async Task Leituras_NoProprioTenant_Funcionam_SemTrilha()
	{
		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);

		foreach (var route in new[]
		{
			$"/api/v1/tenants/{_tenantA}",
			$"/api/v1/tenants/{_tenantA}/users",
			$"/api/v1/tenants/{_tenantA}/users/{_person}",
			$"/api/v1/tenants/{_tenantA}/roles",
			$"/api/v1/tenants/{_tenantA}/roles/{Role}",
			$"/api/v1/tenants/{_tenantA}/roles/{Role}/members",
		})
		{
			(await machine.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.OK, route);
		}

		(await EntriesAsync(_tenantA)).Should().BeEmpty("leitura não vai à trilha");
	}

	[Fact]
	public async Task Escritas_NoProprioTenant_Funcionam_EVaoATrilhaComoClient()
	{
		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);
		var users = $"/api/v1/tenants/{_tenantA}/users/{_person}";
		var roles = $"/api/v1/tenants/{_tenantA}/roles";
		const string newRole = "setor-compras-user";

		(await machine.PostAsJsonAsync(roles, new { name = newRole })).IsSuccessStatusCode.Should().BeTrue("CreateRole");
		(await machine.PutAsJsonAsync($"{roles}/{newRole}/permissions", new { permissions = Array.Empty<string>() }))
			.IsSuccessStatusCode.Should().BeTrue("SetRolePermissions");
		(await machine.PostAsync($"{users}/roles/{newRole}", null)).IsSuccessStatusCode.Should().BeTrue("AddUserRole");
		(await machine.DeleteAsync($"{users}/roles/{newRole}")).IsSuccessStatusCode.Should().BeTrue("RemoveUserRole");
		(await machine.PutAsJsonAsync($"{users}/display-name", new { displayName = "Ana" }))
			.IsSuccessStatusCode.Should().BeTrue("SetUserDisplayName");
		(await machine.PostAsync($"{users}/deactivate", null)).IsSuccessStatusCode.Should().BeTrue("DeactivateUser");
		(await machine.PostAsync($"{users}/activate", null)).IsSuccessStatusCode.Should().BeTrue("ActivateUser");
		(await machine.PostAsync($"{users}/sessions/revoke", null)).IsSuccessStatusCode.Should().BeTrue("RevokeUserSessions");
		(await machine.DeleteAsync($"{roles}/{newRole}")).IsSuccessStatusCode.Should().BeTrue("DeleteRole");

		var records = await EntriesAsync(_tenantA);
		records.Select(record => record.Action.Replace("gestao-do-tenant.", string.Empty, StringComparison.Ordinal)).Should().Equal(
			"CreateRole", "SetRolePermissions", "AddUserRole", "RemoveUserRole", "SetUserDisplayName",
			"DeactivateUser", "ActivateUser", "RevokeUserSessions", "DeleteRole");
		records.Should().AllSatisfy(record =>
		{
			record.ActorType.Should().Be("client", "quem agiu é a máquina");
			record.ActorId.Should().Be(_machineClientId);
			record.TenantId.Should().Be(_tenantA, "a trilha vai sempre para o próprio tenant");
			record.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2), "o instante é o do servidor");
			using var metadata = JsonDocument.Parse(record.Metadata!);
			metadata.RootElement.GetProperty("authority").GetString().Should().Be(SecureGateScopes.TenantManage);
			metadata.RootElement.GetProperty("act").GetString().Should().Be(_machineClientId);
			metadata.RootElement.GetProperty("tenantId").GetGuid().Should().Be(_tenantA);
		});
		secureGate.Auditor.Records.Should().BeEmpty("a gestão por máquina não grava mais no LogStream (ADR-0054)");
	}

	// ---- Âncora: só o tenant do token ----

	[Fact]
	public async Task OutroTenantNaRota_Recusa403_SemTrilha()
	{
		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);

		(await machine.GetAsync($"/api/v1/tenants/{_tenantB}/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await machine.GetAsync($"/api/v1/tenants/{_tenantB}/roles/{Role}/members")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await machine.PostAsJsonAsync($"/api/v1/tenants/{_tenantB}/roles", new { name = "intruso" }))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);

		(await EntriesAsync(_tenantA)).Should().BeEmpty();
		(await EntriesAsync(_tenantB)).Should().BeEmpty();
		(await RoleExistsAsync(_tenantB, "intruso")).Should().BeFalse();
	}

	[Fact]
	public async Task HeaderDeOutroTenant_NaoExecuta()
	{
		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);
		machine.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, _tenantB.ToString());

		var response = await machine.PostAsJsonAsync($"/api/v1/tenants/{_tenantB}/roles", new { name = "intruso" });

		response.IsSuccessStatusCode.Should().BeFalse();
		(await RoleExistsAsync(_tenantB, "intruso")).Should().BeFalse();
	}

	[Fact]
	public async Task TenantDePlataformaNaRota_Recusa403()
	{
		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);

		(await machine.GetAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users"))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task ClientGravadoNoTenantDePlataforma_NaoAdministraAPlataforma()
	{
		// A API nunca cria este client (ADR-0052); simula edição direta do banco — a conferência é a última barreira
		var forged = await MachineClientAsync(SecureGatePlatform.TenantId, SecureGateScopes.TenantManage);
		using var machine = await MachineAsync(forged, SecureGateScopes.TenantManage);

		(await machine.GetAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/users"))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await machine.PostAsJsonAsync($"/api/v1/tenants/{SecureGatePlatform.TenantId}/roles", new { name = "intruso" }))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	// ---- Conta administrativa nunca é alvo ----

	[Fact]
	public async Task AlvoContaAdministrativa_Recusa403_NasRotasProtegidas()
	{
		var adminOfB = await IdentitySeed.UserAsync(secureGate, _tenantA, $"tm-adm-{Guid.NewGuid():N}@secco.test", Role);
		using (var operador = await OperatorAsync())
		{
			(await operador.PutAsync($"/api/v1/tenants/{_tenantB}/admins/{adminOfB}", null))
				.StatusCode.Should().Be(HttpStatusCode.NoContent);
		}

		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);
		var user = $"/api/v1/tenants/{_tenantA}/users/{adminOfB}";

		(await machine.DeleteAsync($"{user}/roles/{Role}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await machine.PutAsJsonAsync($"{user}/display-name", new { displayName = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await machine.PostAsync($"{user}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await machine.PostAsync($"{user}/activate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await machine.PostAsync($"{user}/sessions/revoke", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

		(await EntriesAsync(_tenantA)).Should().BeEmpty();
	}

	// ---- Só as rotas da lista ----

	[Fact]
	public async Task RotaForaDaLista_Recusa403()
	{
		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);
		var tenant = $"/api/v1/tenants/{_tenantA}";

		(await machine.PostAsync($"{tenant}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "DeactivateTenant");
		(await machine.PutAsJsonAsync($"{tenant}/federation", new { directoryId = Guid.NewGuid(), enabled = true }))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden, "UpsertTenantFederation");
		(await machine.PostAsJsonAsync($"{tenant}/clients", new { name = "outra", scopes = new[] { "logstream" } }))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CreateProductClient");
		(await machine.PostAsJsonAsync($"{tenant}/users", new { email = $"novo-{Guid.NewGuid():N}@secco.test" }))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CreateUser");
		(await machine.PostAsJsonAsync("/api/v1/tenants", new { name = "Novo", slug = $"novo-{Guid.NewGuid():N}"[..20] }))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CreateTenant");
		// Rota sem marca nenhuma: só a policy do grupo a protege
		(await machine.PutAsJsonAsync($"{tenant}/databases/logstream", new { connectionString = "Server=intruso;Database=x" }))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden, "UpsertTenantDatabase");
		(await machine.PostAsync($"{tenant}/users/{_person}/password-reset", null))
			.StatusCode.Should().Be(HttpStatusCode.Forbidden, "ResetUserPassword");

		(await EntriesAsync(_tenantA)).Should().BeEmpty();
	}

	[Fact]
	public async Task ClientSemOEscopo_Recusa403()
	{
		// directory:read tem a audiência do próprio SecureGate: o token é aceito e quem decide é a autorização
		var plain = await MachineClientAsync(_tenantA, SecureGateScopes.DirectoryRead);
		using var machine = await MachineAsync(plain, SecureGateScopes.DirectoryRead);

		(await machine.GetAsync($"/api/v1/tenants/{_tenantA}/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	// ---- Trilha fail-closed ----

	[Fact]
	public async Task SemAuditoriaDoLogStreamConfigurada_EscritaFunciona_EGravaALocal()
	{
		// ADR-0054: a gestão por máquina não depende de SecureGate:Audit nem do LogStream
		secureGate.Auditor.IsConfigured = false;
		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);

		(await machine.GetAsync($"/api/v1/tenants/{_tenantA}/roles")).StatusCode.Should().Be(HttpStatusCode.OK);
		(await machine.PostAsJsonAsync($"/api/v1/tenants/{_tenantA}/roles", new { name = "sem-logstream" }))
			.IsSuccessStatusCode.Should().BeTrue();

		(await RoleExistsAsync(_tenantA, "sem-logstream")).Should().BeTrue();
		var entry = (await EntriesAsync(_tenantA)).Should().ContainSingle().Subject;
		entry.Action.Should().Be("gestao-do-tenant.CreateRole");
		entry.ActorId.Should().Be(_machineClientId);
		secureGate.Auditor.Records.Should().BeEmpty();
	}

	[Fact]
	public async Task TrilhaRecusada_Responde503_ENadaEEscrito()
	{
		secureGate.FailTenantManageAudit = true;
		using var machine = await MachineAsync(_machineClientId, SecureGateScopes.TenantManage);

		(await machine.PostAsJsonAsync($"/api/v1/tenants/{_tenantA}/roles", new { name = "trilha-fora" }))
			.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

		(await RoleExistsAsync(_tenantA, "trilha-fora")).Should().BeFalse();
		(await EntriesAsync(_tenantA)).Should().BeEmpty();
	}

	// ---- Apoio ----

	private async Task<string> MachineClientAsync(Guid tenantId, params string[] scopes)
	{
		var clientId = $"cli_{Guid.NewGuid():N}"[..20];
		await secureGate.CreateProductClientAsync(tenantId, clientId, Secret, roles: null, scopes);

		return clientId;
	}

	private Task<HttpClient> MachineAsync(string clientId, string scope) => BearerAsync(clientId, Secret, scope);

	private Task<HttpClient> OperatorAsync() => BearerAsync(OperatorClientId, OperatorSecret, SecureGateScopes.Admin);

	private async Task<HttpClient> BearerAsync(string clientId, string secret, string scope)
	{
		using var client = secureGate.CreateClient();
		var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = secret,
			["scope"] = scope,
		}));
		response.EnsureSuccessStatusCode();

		using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		var bearer = secureGate.CreateClient();
		bearer.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", payload.RootElement.GetProperty("access_token").GetString());

		return bearer;
	}

	private async Task<IReadOnlyList<AuthorityAuditEntry>> EntriesAsync(Guid tenantId)
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.AuthorityAuditEntries.AsNoTracking()
			.Where(entry => entry.TenantId == tenantId)
			.OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id)
			.ToListAsync();
	}

	private async Task<bool> RoleExistsAsync(Guid tenantId, string name)
	{
		using var operador = await OperatorAsync();
		var roles = await operador.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/roles", Json);

		return roles.EnumerateArray().Any(role => role.GetProperty("name").GetString() == name);
	}
}
