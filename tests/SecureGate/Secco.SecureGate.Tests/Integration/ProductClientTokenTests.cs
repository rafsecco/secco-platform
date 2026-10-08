using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Emissão de token para client de produto vinculado a tenant (ADR-0037).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ProductClientTokenTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private const string Secret = "product-client-secret-de-32-chars-min!!";
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string NewClientId() => $"cli_{Guid.NewGuid():N}"[..20];

	private Task<HttpResponseMessage> RequestTokenAsync(string clientId, string? scope)
	{
		var form = new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = Secret,
		};

		if (scope is not null)
		{
			form["scope"] = scope;
		}

		return factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(form));
	}

	private static async Task<JsonWebToken> ReadTokenAsync(HttpResponseMessage response)
	{
		var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
		return new JsonWebTokenHandler().ReadJsonWebToken(payload.GetProperty("access_token").GetString());
	}

	[Fact]
	public async Task ClientCredentials_ClientDeProduto_TokenSaiComTenantIdERole()
	{
		var clientId = NewClientId();
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, "compras-writer", "logstream");

		var response = await RequestTokenAsync(clientId, "logstream");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var token = await ReadTokenAsync(response);
		token.GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString());
		token.GetClaim("role").Value.Should().Be("compras-writer");
		token.GetClaim("sub").Value.Should().Be(clientId);
	}

	[Fact]
	public async Task ClientCredentials_ClientDeProdutoSemScope_TokenSaiComTenantId()
	{
		var clientId = NewClientId();
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, roles: null, "logstream");

		var response = await RequestTokenAsync(clientId, scope: null);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await ReadTokenAsync(response)).GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString());
	}

	[Fact]
	public async Task ClientCredentials_ClientDePlataforma_TokenSaiSemTenantId()
	{
		var clientId = $"plataforma-{Guid.NewGuid():N}"[..30];
		await factory.CreateClientAsync(clientId, Secret, "logstream");

		var response = await RequestTokenAsync(clientId, "logstream");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await ReadTokenAsync(response)).Claims.Should().NotContain(c => c.Type == "tenant_id");
	}

	[Fact]
	public async Task ClientCredentials_TenantDesativado_RecusaComInvalidClient()
	{
		var clientId = NewClientId();
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, roles: null, "logstream");

		using (var scope = factory.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			var tenant = await context.Tenants.FindAsync(_tenantId);
			tenant!.Deactivate();
			await context.SaveChangesAsync();
		}

		var response = await RequestTokenAsync(clientId, "logstream");

		response.IsSuccessStatusCode.Should().BeFalse();
		(await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
	}

	[Fact]
	public async Task ClientCredentials_EscopoDeInfraestruturaGravadoNoBanco_RecusaNaEmissao()
	{
		var clientId = NewClientId();
		// Simula edição direta do banco: a API nunca grava isto (Task 4), a emissão precisa barrar mesmo assim
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, roles: null,
			"logstream", Secco.SecureGate.Application.SecureGateScopes.Admin);

		var response = await RequestTokenAsync(clientId, Secco.SecureGate.Application.SecureGateScopes.Admin);

		response.IsSuccessStatusCode.Should().BeFalse();
		(await response.Content.ReadAsStringAsync()).Should().Contain("invalid_scope");
	}

	[Fact]
	public async Task Resolucao_PapelHomonimoEmOutroTenant_UsaSoOTenantDoToken()
	{
		// Homônimos criados ANTES e DEPOIS do papel do tenant do token: um resolvedor que ignore o
		// tenant devolveria o de outro tenant qualquer que fosse a ordem de leitura do banco.
		var tenantBefore = await IdentitySeed.TenantAsync(factory);
		var tenantAfter = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantBefore, "homonimo", "log-entries:write");
		await IdentitySeed.RoleAsync(factory, _tenantId, "homonimo", "log-entries:read");
		await IdentitySeed.RoleAsync(factory, tenantAfter, "homonimo", "log-entries:write");

		var clientId = NewClientId();
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, "homonimo", "logstream");
		var token = await ReadTokenAsync(await RequestTokenAsync(clientId, "logstream"));
		var tokenTenant = token.GetClaim("tenant_id").Value;

		var reader = factory.CreateClient();
		reader.DefaultRequestHeaders.Authorization = new("Bearer",
			factory.CreateTokenWithScopes(Secco.SecureGate.Application.SecureGateScopes.AuthorizationRead));
		var permissions = await reader.GetFromJsonAsync<string[]>(
			$"/api/v1/authorization/tenants/{tokenTenant}/roles/homonimo/permissions", Json);

		permissions.Should().Equal("log-entries:read");
	}
}
