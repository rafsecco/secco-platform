extern alias logstream;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Infrastructure;
using Secco.SharedKernel.Constants;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Client de produto contido no próprio tenant por um produto REAL (ADR-0037): o LogStream valida
/// o token do SecureGate e aplica a regra de conflito claim × header do SDK, sem mudança nenhuma.
/// </summary>
public class ProductClientCrossProductTests(SecureGateApiFactory secureGate)
	: IClassFixture<SecureGateApiFactory>, IAsyncLifetime
{
	private const string Secret = "product-client-secret-de-32-chars-min!!";
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantA;
	private Guid _tenantB;
	private string _clientId = null!;
	private LogStreamHost _logStream = null!;

	private sealed class LogStreamHost(SecureGateApiFactory secureGate, Guid tenantA, Guid tenantB)
		: WebApplicationFactory<logstream::Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.UseEnvironment("Testing");

			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Secco:Authentication:Audience"] = "secco-logstream",
					["Secco:Authentication:Authority"] = "http://localhost",
					["Secco:Authentication:RequireHttpsMetadata"] = "false",
					[$"Secco:Tenancy:Tenants:{tenantA}:ConnectionString"] = secureGate.GetConnectionStringFor("secco_logstream_pc_a"),
					[$"Secco:Tenancy:Tenants:{tenantB}:ConnectionString"] = secureGate.GetConnectionStringFor("secco_logstream_pc_b"),
					["Secco:Authorization:Roles:writer:Permissions:0"] = "log-entries:read",
					["Secco:Authorization:Roles:writer:Permissions:1"] = "log-entries:write",
				}));

			builder.ConfigureServices(services =>
				services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
					options.BackchannelHttpHandler = secureGate.Server.CreateHandler()));
		}
	}

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		_tenantA = await IdentitySeed.TenantAsync(secureGate);
		_tenantB = await IdentitySeed.TenantAsync(secureGate);
		_clientId = $"cli_{Guid.NewGuid():N}"[..20];
		await secureGate.CreateProductClientAsync(_tenantA, _clientId, Secret, "writer", "logstream");

		_logStream = new LogStreamHost(secureGate, _tenantA, _tenantB);
		await _logStream.Services.MigrateLogStreamTenantDatabasesAsync();
	}

	public async Task DisposeAsync() => await _logStream.DisposeAsync();

	private async Task<string> TokenAsync()
	{
		var response = await secureGate.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(
			new Dictionary<string, string>
			{
				["grant_type"] = "client_credentials",
				["client_id"] = _clientId,
				["client_secret"] = Secret,
				["scope"] = "logstream",
			}));
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString()!;
	}

	private async Task<HttpResponseMessage> WriteLogAsync(Guid? headerTenant)
	{
		var client = _logStream.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());

		if (headerTenant is { } tenant)
		{
			client.DefaultRequestHeaders.Add(SeccoHeaders.TenantId, tenant.ToString());
		}

		return await client.PostAsJsonAsync("/api/v1/log-entries", new { level = "Information", message = "compras" });
	}

	[Fact]
	public async Task ClientDeProduto_NoProprioTenant_Grava()
	{
		// Sem header: o tenant vem só do claim tenant_id (o client de produto não precisa saber dele)
		(await WriteLogAsync(headerTenant: null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
	}

	[Fact]
	public async Task ClientDeProduto_HeaderIgualAoClaim_Grava()
	{
		(await WriteLogAsync(_tenantA)).StatusCode.Should().Be(HttpStatusCode.Accepted);
	}

	[Fact]
	public async Task ClientDeProduto_HeaderDeOutroTenant_400()
	{
		(await WriteLogAsync(_tenantB)).StatusCode.Should().Be(HttpStatusCode.BadRequest,
			"tenant_id no token × X-Tenant-Id divergente é conflito no TenantResolver (ADR-0005/0037)");
	}
}
