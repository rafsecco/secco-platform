extern alias logstream;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Application;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application;
using Secco.SecureGate.Client.Permissions;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Publicação do catálogo no <c>migrate</c> do produto (ADR-0039): o LogStream real, com
/// <c>Secco:SecureGate</c> apontando para o SecureGate de teste, publica o seu <c>Catalog</c> ao rodar o
/// seed de referência; sem a seção (compose, DEV isolado), o mesmo seed não falha.
/// </summary>
[Collection(SelfIssuedApiCollectionDefinition.Name)]
public class PermissionCatalogPublishE2ETests(SelfIssuedAuthSecureGateApiFactory secureGate) : IAsyncLifetime
{
	private const string ClientId = "logstream-catalog-publisher";
	private const string AdminClientId = "logstream-catalog-admin";
	private const string AdminClientSecret = "logstream-catalog-admin-secret-32-chars!!";
	private const string ClientSecret = "logstream-catalog-publisher-secret-32-chars!";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();

		// Client de plataforma só com o escopo de publicação (least privilege)
		await secureGate.CreateClientAsync(ClientId, ClientSecret, SecureGateScopes.PermissionsFor("logstream"));
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private sealed class LogStreamHost(SelfIssuedAuthSecureGateApiFactory secureGate, bool withSecureGate)
		: WebApplicationFactory<logstream::Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.UseEnvironment("Testing");

			builder.ConfigureAppConfiguration((_, configuration) =>
			{
				var settings = new Dictionary<string, string?>
				{
					["Secco:Authentication:Audience"] = "secco-logstream",
					["Secco:Authentication:Authority"] = "http://localhost",
					["Secco:Authentication:RequireHttpsMetadata"] = "false",
				};

				if (withSecureGate)
				{
					settings["Secco:SecureGate:BaseUrl"] = "http://localhost";
					settings["Secco:SecureGate:ClientId"] = ClientId;
					settings["Secco:SecureGate:ClientSecret"] = ClientSecret;
					// O LogStream também resolve o catálogo de tenants por esta seção, que exige o produto
					settings["Secco:SecureGate:Product"] = "logstream";
				}

				configuration.AddInMemoryCollection(settings);
			});

			builder.ConfigureServices(services =>
				// Token e PUT trafegam pelo handler in-memory do TestServer do SecureGate
				services.AddHttpClient(SecureGatePermissionCatalogPublisher.HttpClientName)
					.ConfigurePrimaryHttpMessageHandler(() => secureGate.Server.CreateHandler()));
		}
	}

	private static async Task<string> TokenAsync(HttpClient client, string clientId, string clientSecret, string scope)
	{
		var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = clientSecret,
			["scope"] = scope,
		}));
		response.EnsureSuccessStatusCode();

		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString()!;
	}

	[Fact]
	public async Task Migrate_WithSecureGateConfigured_PublishesTheWholeCatalog()
	{
		await using var logStream = new LogStreamHost(secureGate, withSecureGate: true);

		await logStream.Services.SeedSeccoDataAsync();

		await secureGate.CreateClientAsync(AdminClientId, AdminClientSecret, SecureGateScopes.Admin);

		using var admin = secureGate.CreateClient();
		admin.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", await TokenAsync(admin, AdminClientId, AdminClientSecret, SecureGateScopes.Admin));

		var published = await admin.GetFromJsonAsync<JsonElement>("/api/v1/permission-catalog/logstream", Json);

		var names = published.GetProperty("permissions").EnumerateArray()
			.Select(entry => entry.GetProperty("name").GetString()!)
			.ToList();

		names.Should().BeEquivalentTo(LogStreamPermissions.Catalog.Select(p => p.Name));
	}

	[Fact]
	public async Task Migrate_WithoutSecureGateSection_DoesNotThrow()
	{
		await using var logStream = new LogStreamHost(secureGate, withSecureGate: false);

		var act = () => logStream.Services.SeedSeccoDataAsync();

		await act.Should().NotThrowAsync("sem Secco:SecureGate o publicador pula (compose, DEV isolado)");
	}
}
