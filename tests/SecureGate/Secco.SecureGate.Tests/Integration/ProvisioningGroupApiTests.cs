using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Application;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Contrato do provisionamento pelo diretório (ADR-0055, entrega B): marcar e desmarcar o grupo de provisionamento
/// (idempotentes, no máximo um por tenant, garantido também pelo banco), domínios de e-mail da federação e o estado
/// "gerida pelo diretório" / origem da desativação na leitura de usuários.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ProvisioningGroupApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
	private static readonly string[] MixedCaseDomains = ["Empresa.com", "empresa.com.br", "empresa.com"];
	private static readonly string[] SingleDomain = ["empresa.com"];

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Client(params string[] scopes)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(scopes));

		return client;
	}

	private HttpClient Admin() => Client(SecureGateScopes.Admin);

	private sealed record Seed(Guid TenantId, Guid FirstMapping, Guid SecondMapping);

	/// <summary>Tenant com federação e dois grupos mapeados (a mesma pessoa que o operador teria feito pela API).</summary>
	private async Task<Seed> SeedAsync()
	{
		var tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantId, "base");
		await IdentitySeed.RoleAsync(factory, tenantId, "extra");

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		context.TenantFederations.Add(new TenantFederation(tenantId, Guid.NewGuid()));
		var baseRole = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == "BASE");
		var extraRole = await context.Roles.FirstAsync(r => r.TenantId == tenantId && r.NormalizedName == "EXTRA");
		var first = new TenantGroupRoleMapping(tenantId, Guid.NewGuid(), "Todos", baseRole.Id);
		var second = new TenantGroupRoleMapping(tenantId, Guid.NewGuid(), "Financeiro", extraRole.Id);
		context.TenantGroupRoleMappings.AddRange(first, second);
		await context.SaveChangesAsync();

		return new Seed(tenantId, first.Id, second.Id);
	}

	private static string Route(Guid tenantId, Guid mappingId) =>
		$"/api/v1/tenants/{tenantId}/entra/group-role-mappings/{mappingId}/provisioning";

	private static async Task<bool> IsProvisioningAsync(HttpClient admin, Guid tenantId, Guid mappingId)
	{
		var list = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}/entra/group-role-mappings", Json);

		return list.EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == mappingId).GetProperty("isProvisioningGroup").GetBoolean();
	}

	// ---- Marcar e desmarcar ----

	[Fact]
	public async Task Marcar_E204_AparecerNaListaDeMapeamentos_ERepetirEIdempotente()
	{
		var seed = await SeedAsync();
		var admin = Admin();

		(await IsProvisioningAsync(admin, seed.TenantId, seed.FirstMapping)).Should().BeFalse("nasce desmarcado");

		(await admin.PutAsync(Route(seed.TenantId, seed.FirstMapping), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await admin.PutAsync(Route(seed.TenantId, seed.FirstMapping), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		(await IsProvisioningAsync(admin, seed.TenantId, seed.FirstMapping)).Should().BeTrue();
	}

	[Fact]
	public async Task Marcar_OutroGrupoQuandoJaHaUm_E409_ENaoMuda()
	{
		var seed = await SeedAsync();
		var admin = Admin();
		(await admin.PutAsync(Route(seed.TenantId, seed.FirstMapping), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var second = await admin.PutAsync(Route(seed.TenantId, seed.SecondMapping), null);

		second.StatusCode.Should().Be(HttpStatusCode.Conflict, "no máximo um grupo de provisionamento por tenant");
		(await IsProvisioningAsync(admin, seed.TenantId, seed.FirstMapping)).Should().BeTrue();
		(await IsProvisioningAsync(admin, seed.TenantId, seed.SecondMapping)).Should().BeFalse();
	}

	[Fact]
	public async Task Desmarcar_E204_Idempotente_ELiberaOutroGrupoParaSerMarcado()
	{
		var seed = await SeedAsync();
		var admin = Admin();
		await admin.PutAsync(Route(seed.TenantId, seed.FirstMapping), null);

		(await admin.DeleteAsync(Route(seed.TenantId, seed.FirstMapping))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await admin.DeleteAsync(Route(seed.TenantId, seed.FirstMapping))).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await IsProvisioningAsync(admin, seed.TenantId, seed.FirstMapping)).Should().BeFalse();

		(await admin.PutAsync(Route(seed.TenantId, seed.SecondMapping), null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await IsProvisioningAsync(admin, seed.TenantId, seed.SecondMapping)).Should().BeTrue();
	}

	[Fact]
	public async Task MapeamentoInexistenteOuDeOutroTenant_E404_NaDuasOperacoes()
	{
		var seed = await SeedAsync();
		var other = await SeedAsync();
		var admin = Admin();

		(await admin.PutAsync(Route(seed.TenantId, Guid.NewGuid()), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await admin.DeleteAsync(Route(seed.TenantId, Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await admin.PutAsync(Route(seed.TenantId, other.FirstMapping), null)).StatusCode
			.Should().Be(HttpStatusCode.NotFound, "o mapeamento de outro tenant responde como inexistente");
		(await IsProvisioningAsync(admin, other.TenantId, other.FirstMapping)).Should().BeFalse();
	}

	[Fact]
	public async Task SemOEscopoDeAdmin_Responde403_NasDuasOperacoes()
	{
		var seed = await SeedAsync();
		var semEscopo = Client("logstream:read");

		(await semEscopo.PutAsync(Route(seed.TenantId, seed.FirstMapping), null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await semEscopo.DeleteAsync(Route(seed.TenantId, seed.FirstMapping))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
		(await IsProvisioningAsync(Admin(), seed.TenantId, seed.FirstMapping)).Should().BeFalse();
	}

	[Fact]
	public void RotasDeProvisionamento_NaoSaoAbertasAAdminDeTenantNemAGestaoPorMaquina()
	{
		// Quem decide que o diretório passa a mandar em quem tem conta é o operador (ADR-0051): nenhuma autoridade
		// delegada alcança estas rotas
		var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
			.Where(e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName is "MarkProvisioningGroup" or "UnmarkProvisioningGroup")
			.ToList();

		endpoints.Should().HaveCount(2);
		endpoints.Should().OnlyContain(e => e.Metadata.GetMetadata<TenantAdminAllowedMetadata>() == null);
		endpoints.Should().OnlyContain(e => e.Metadata.GetMetadata<TenantManageAllowedMetadata>() == null);
	}

	[Fact]
	public async Task OBanco_BarraUmSegundoGrupoDeProvisionamentoNoMesmoTenant_MasPermiteEmOutroTenant()
	{
		var seed = await SeedAsync();
		var other = await SeedAsync();

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var first = await context.TenantGroupRoleMappings.FirstAsync(m => m.Id == seed.FirstMapping);
		var second = await context.TenantGroupRoleMappings.FirstAsync(m => m.Id == seed.SecondMapping);
		first.MarkAsProvisioningGroup();
		second.MarkAsProvisioningGroup();

		var act = () => context.SaveChangesAsync();

		await act.Should().ThrowAsync<DbUpdateException>("o índice único filtrado garante um por tenant mesmo sem a aplicação");

		// Outro tenant é outra história: um por tenant, não um por instalação
		using var scope2 = factory.Services.CreatePlatformScope();
		var context2 = scope2.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		(await context2.TenantGroupRoleMappings.FirstAsync(m => m.Id == other.FirstMapping)).MarkAsProvisioningGroup();
		(await context2.TenantGroupRoleMappings.FirstAsync(m => m.Id == seed.FirstMapping)).MarkAsProvisioningGroup();
		await context2.SaveChangesAsync();
	}

	// ---- Domínios permitidos ----

	[Fact]
	public async Task Federacao_DominiosNormalizadosNaLeitura_OmitirMantem_VazioEsvazia()
	{
		var seed = await SeedAsync();
		var admin = Admin();
		var directory = Guid.NewGuid();
		var route = $"/api/v1/tenants/{seed.TenantId}/federation";

		(await admin.PutAsJsonAsync(route, new { directoryId = directory, enabled = true, allowedEmailDomains = MixedCaseDomains }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await Domains(admin, seed.TenantId)).Should().Equal("empresa.com", "empresa.com.br");

		(await admin.PutAsJsonAsync(route, new { directoryId = directory, enabled = false }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await Domains(admin, seed.TenantId)).Should().Equal("empresa.com", "empresa.com.br");

		(await admin.PutAsJsonAsync(route, new { directoryId = directory, enabled = true, allowedEmailDomains = Array.Empty<string>() }))
			.StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await Domains(admin, seed.TenantId)).Should().BeEmpty();
	}

	[Theory]
	[InlineData("*.empresa.com")]
	[InlineData("empresa")]
	[InlineData("a@empresa.com")]
	[InlineData("empresa.com/x")]
	public async Task Federacao_DominioInvalido_E400_ENaoMudaALista(string domain)
	{
		var seed = await SeedAsync();
		var admin = Admin();
		var directory = Guid.NewGuid();
		var route = $"/api/v1/tenants/{seed.TenantId}/federation";
		await admin.PutAsJsonAsync(route, new { directoryId = directory, enabled = true, allowedEmailDomains = SingleDomain });

		var response = await admin.PutAsJsonAsync(route, new { directoryId = directory, enabled = true, allowedEmailDomains = new[] { domain } });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await Domains(admin, seed.TenantId)).Should().Equal("empresa.com");
	}

	[Fact]
	public async Task Federacao_MaisDominiosQueOTeto_E400()
	{
		var seed = await SeedAsync();
		var admin = Admin();
		var many = Enumerable.Range(0, EmailDomainRules.MaxCount + 1).Select(i => $"d{i}.com").ToArray();

		var response = await admin.PutAsJsonAsync(
			$"/api/v1/tenants/{seed.TenantId}/federation",
			new { directoryId = Guid.NewGuid(), enabled = true, allowedEmailDomains = many });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	private static async Task<string[]> Domains(HttpClient admin, Guid tenantId)
	{
		var tenant = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{tenantId}", Json);

		return [.. tenant.GetProperty("federation").GetProperty("allowedEmailDomains").EnumerateArray().Select(d => d.GetString()!)];
	}

	// ---- Estado da conta na leitura ----

	[Fact]
	public async Task Usuario_ExpoeContaGeridaEOrigemDaDesativacao_NaListaNoDetalheENosMembrosDoPerfil()
	{
		var seed = await SeedAsync();
		var admin = Admin();
		var userId = await IdentitySeed.UserAsync(factory, seed.TenantId, $"gerida-{Guid.NewGuid():N}@secco.test", "base");

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(set => set
				.SetProperty(u => u.DirectoryManaged, true)
				.SetProperty(u => u.DeactivationOrigin, Application.Users.DeactivationOrigin.Directory)
				.SetProperty(u => u.LockoutEnabled, true)
				.SetProperty(u => u.LockoutEnd, DateTimeOffset.MaxValue));
		}

		var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{seed.TenantId}/users/{userId}", Json);
		detail.GetProperty("directoryManaged").GetBoolean().Should().BeTrue();
		detail.GetProperty("deactivationOrigin").GetString().Should().Be("Directory");

		var list = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{seed.TenantId}/users", Json);
		var row = list.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == userId);
		row.GetProperty("directoryManaged").GetBoolean().Should().BeTrue();
		row.GetProperty("deactivationOrigin").GetString().Should().Be("Directory");

		var members = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{seed.TenantId}/roles/base/members", Json);
		var member = members.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == userId);
		member.GetProperty("directoryManaged").GetBoolean().Should().BeTrue();
		member.GetProperty("deactivationOrigin").GetString().Should().Be("Directory");
	}

	[Fact]
	public async Task DesativacaoManual_GravaOrigemManual_AAtivacaoManualLimpa()
	{
		var seed = await SeedAsync();
		var admin = Admin();
		var userId = await IdentitySeed.UserAsync(factory, seed.TenantId, $"manual-{Guid.NewGuid():N}@secco.test");
		var route = $"/api/v1/tenants/{seed.TenantId}/users/{userId}";

		(await admin.PostAsync($"{route}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		var deactivated = await admin.GetFromJsonAsync<JsonElement>(route, Json);
		deactivated.GetProperty("status").GetString().Should().Be("Deactivated");
		deactivated.GetProperty("deactivationOrigin").GetString().Should().Be("Manual");
		deactivated.GetProperty("directoryManaged").GetBoolean().Should().BeFalse();

		(await admin.PostAsync($"{route}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		var active = await admin.GetFromJsonAsync<JsonElement>(route, Json);
		active.GetProperty("status").GetString().Should().Be("Active");
		active.GetProperty("deactivationOrigin").GetString().Should().Be("None");
	}

	[Fact]
	public async Task DesativacaoManualDeContaDesativadaPeloDiretorio_ViraManual_ASincronizacaoNaoMaisAReativa()
	{
		var seed = await SeedAsync();
		var admin = Admin();
		var userId = await IdentitySeed.UserAsync(factory, seed.TenantId, $"assume-{Guid.NewGuid():N}@secco.test");

		using (var scope = factory.Services.CreatePlatformScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			await context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(set => set
				.SetProperty(u => u.DirectoryManaged, true)
				.SetProperty(u => u.DeactivationOrigin, Application.Users.DeactivationOrigin.Directory)
				.SetProperty(u => u.LockoutEnabled, true)
				.SetProperty(u => u.LockoutEnd, DateTimeOffset.MaxValue));
		}

		(await admin.PostAsync($"/api/v1/tenants/{seed.TenantId}/users/{userId}/deactivate", null)).StatusCode
			.Should().Be(HttpStatusCode.NoContent);

		var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/tenants/{seed.TenantId}/users/{userId}", Json);
		detail.GetProperty("deactivationOrigin").GetString().Should().Be("Manual", "o admin assumiu a decisão");
	}
}
