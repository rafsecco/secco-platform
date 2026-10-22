using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.TenantAdministration;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Trilha de autoridade guardada no SecureGate (ADR-0054), ponta a ponta: a leitura é só do operador e só do tenant
/// da rota, o isolamento vale no escopo do tenant, e a retenção opt-in apaga só o vencido.
/// </summary>
[Collection(TenantAdminApiCollectionDefinition.Name)]
public class AuthorityAuditTrailTests(TenantAdminSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string Secret = "authority-audit-client-secret-32-chars!";
	private const string OperatorClientId = "authority-audit-operator-machine";
	private const string OperatorSecret = "authority-audit-operator-machine-secret!";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantA;
	private Guid _tenantB;
	private string _machineA = string.Empty;
	private string _machineB = string.Empty;

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		await secureGate.CreateClientAsync(OperatorClientId, OperatorSecret, SecureGateScopes.Admin);
		secureGate.Auditor.Reset();
		secureGate.FailTenantManageAudit = false;

		_tenantA = await IdentitySeed.TenantAsync(secureGate);
		_tenantB = await IdentitySeed.TenantAsync(secureGate);
		_machineA = await MachineClientAsync(_tenantA);
		_machineB = await MachineClientAsync(_tenantB);
	}

	public Task DisposeAsync()
	{
		secureGate.FailTenantManageAudit = false;

		return Task.CompletedTask;
	}

	// ---- Leitura: só o operador, só o tenant da rota ----

	[Fact]
	public async Task Operador_ListaSoOTenantDaRota_DoMaisRecenteParaOMaisAntigo_Paginado()
	{
		await WriteAsRoleAsync(_machineA, _tenantA, "primeiro");
		await WriteAsRoleAsync(_machineA, _tenantA, "segundo");
		await WriteAsRoleAsync(_machineA, _tenantA, "terceiro");
		await WriteAsRoleAsync(_machineB, _tenantB, "so-do-b");

		using var operador = await BearerAsync(OperatorClientId, OperatorSecret, SecureGateScopes.Admin);

		var page1 = await operador.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantA}/audit-entries?page=1&size=2", Json);
		var page2 = await operador.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantA}/audit-entries?page=2&size=2", Json);

		page1.GetProperty("totalCount").GetInt64().Should().Be(3, "só as três do tenant A");
		page1.GetProperty("items").EnumerateArray().Select(Action).Should().Equal("gestao-do-tenant.CreateRole", "gestao-do-tenant.CreateRole");
		var all = page1.GetProperty("items").EnumerateArray().Concat(page2.GetProperty("items").EnumerateArray()).ToList();
		all.Should().HaveCount(3);
		all.Select(item => item.GetProperty("occurredAt").GetDateTimeOffset()).Should().BeInDescendingOrder();
		all.Should().OnlyContain(item => item.GetProperty("actorId").GetString() == _machineA && item.GetProperty("actorType").GetString() == "client");
	}

	[Fact]
	public async Task EntradaDoTenantA_NaoApareceLendoB_NemNoEscopoDeB()
	{
		await WriteAsRoleAsync(_machineA, _tenantA, "isolada");

		using var operador = await BearerAsync(OperatorClientId, OperatorSecret, SecureGateScopes.Admin);
		var viaB = await operador.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{_tenantB}/audit-entries", Json);

		viaB.GetProperty("totalCount").GetInt64().Should().Be(0);

		// Camada de baixo: o filtro "Tenant" e o RLS, no escopo do tenant B, também não a enxergam
		using var scope = secureGate.Services.CreateScope();
		scope.ServiceProvider.GetRequiredService<ITenantAccessScope>().EnterTenant(_tenantB);
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context.AuthorityAuditEntries.AsNoTracking().ToListAsync()).Should().BeEmpty();
	}

	[Fact]
	public async Task GestaoPorMaquina_NaoLeAPropriaTrilha_NemUmaDeOutroTenant()
	{
		await WriteAsRoleAsync(_machineA, _tenantA, "lida-so-pelo-operador");
		using var machineA = await BearerAsync(_machineA, Secret, SecureGateScopes.TenantManage);

		(await machineA.GetAsync($"/api/v1/tenants/{_tenantA}/audit-entries")).StatusCode
			.Should().Be(HttpStatusCode.Forbidden, "a trilha existe contra o client");
		(await machineA.GetAsync($"/api/v1/tenants/{_tenantB}/audit-entries")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Fact]
	public async Task SemToken_Responde401_ETenantInexistente_Responde404()
	{
		(await secureGate.CreateClient().GetAsync($"/api/v1/tenants/{_tenantA}/audit-entries")).StatusCode
			.Should().Be(HttpStatusCode.Unauthorized);

		using var operador = await BearerAsync(OperatorClientId, OperatorSecret, SecureGateScopes.Admin);
		(await operador.GetAsync($"/api/v1/tenants/{Guid.NewGuid()}/audit-entries")).StatusCode
			.Should().Be(HttpStatusCode.NotFound);
	}

	// ---- Retenção opt-in ----

	[Fact]
	public async Task Retencao_ApagaSoOVencido_PeloInstanteDoServidor_EDeTodosOsTenants()
	{
		var old = DateTimeOffset.UtcNow.AddDays(-40);
		var recent = DateTimeOffset.UtcNow.AddDays(-5);
		var oldA = await InsertAsync(_tenantA, old);
		var oldB = await InsertAsync(_tenantB, old);
		var recentA = await InsertAsync(_tenantA, recent);

		var deleted = await RetentionService(retentionDays: 30).RunOnceAsync();

		deleted.Should().BeGreaterThanOrEqualTo(2);
		(await ExistsAsync(oldA)).Should().BeFalse();
		(await ExistsAsync(oldB)).Should().BeFalse();
		(await ExistsAsync(recentA)).Should().BeTrue("dentro da janela não sai");

		// Idempotente: a segunda passada não encontra o que apagar
		(await RetentionService(retentionDays: 30).RunOnceAsync()).Should().Be(0);
	}

	[Theory]
	[InlineData(null)]
	[InlineData(0)]
	[InlineData(-3)]
	[InlineData(3651)]
	public async Task Retencao_SemValorOuForaDaFaixa_NaoApagaNada(int? retentionDays)
	{
		var ancient = await InsertAsync(_tenantA, DateTimeOffset.UtcNow.AddDays(-4000));

		(await RetentionService(retentionDays).RunOnceAsync()).Should().Be(0);

		(await ExistsAsync(ancient)).Should().BeTrue("sem janela válida a trilha não expira");
	}

	// ---- Apoio ----

	private static string Action(JsonElement item) => item.GetProperty("action").GetString()!;

	private AuthorityAuditRetentionService RetentionService(int? retentionDays) => new(
		secureGate.Services.GetRequiredService<IServiceScopeFactory>(),
		Options.Create(new AuthorityAuditOptions { RetentionDays = retentionDays }),
		TimeProvider.System,
		NullLogger<AuthorityAuditRetentionService>.Instance);

	private async Task<Guid> InsertAsync(Guid tenantId, DateTimeOffset at)
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var entry = new AuthorityAuditEntry(
			tenantId, "client", "cli-retencao", null, "gestao-do-tenant.CreateRole", "tenant", tenantId.ToString(), null, at);
		context.AuthorityAuditEntries.Add(entry);
		await context.SaveChangesAsync();

		return entry.Id;
	}

	private async Task<bool> ExistsAsync(Guid id)
	{
		using var scope = secureGate.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		return await context.AuthorityAuditEntries.AsNoTracking().AnyAsync(e => e.Id == id);
	}

	private async Task WriteAsRoleAsync(string clientId, Guid tenantId, string roleName)
	{
		using var machine = await BearerAsync(clientId, Secret, SecureGateScopes.TenantManage);
		var response = await machine.PostAsJsonAsync($"/api/v1/tenants/{tenantId}/roles", new { name = roleName });

		response.IsSuccessStatusCode.Should().BeTrue();
	}

	private async Task<string> MachineClientAsync(Guid tenantId)
	{
		var clientId = $"cli_{Guid.NewGuid():N}"[..20];
		await secureGate.CreateProductClientAsync(tenantId, clientId, Secret, roles: null, SecureGateScopes.TenantManage);

		return clientId;
	}

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
}
