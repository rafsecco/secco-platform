using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Fábrica com automação de provisionamento ligada, apontando para a própria instância de SQL
/// Server da suíte — é o único jeito honesto de testar DDL: contra um servidor real.
/// </summary>
public sealed class ProvisioningSecureGateApiFactory : SecureGateApiFactory
{
	/// <summary>
	/// Connection string privilegiada da instância de teste. Derivada da do banco de plataforma
	/// trocando o catálogo: <c>GetConnectionStringFor</c> sufixa o nome do database com o
	/// identificador da execução, e <c>master</c> é nome fixo do servidor — pedi-lo por lá
	/// devolveria <c>master_&lt;sufixo&gt;</c>, que não existe.
	/// </summary>
	public string AdminConnectionString =>
		new SqlConnectionStringBuilder(GetPlatformConnectionString()) { InitialCatalog = "master" }
			.ConnectionString;

	/// <summary>
	/// Endereço do SQL Server da instância de teste. O nome não é <c>Server</c> de propósito:
	/// aquilo esconderia o <c>WebApplicationFactory.Server</c>, que é o servidor HTTP de teste —
	/// dois "servidores" com significados opostos no mesmo tipo.
	/// </summary>
	public string SqlServerAddress => new SqlConnectionStringBuilder(AdminConnectionString).DataSource;

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		base.ConfigureTestConfiguration(settings);

		settings["SecureGate:Provisioning:Targets:default:Provider"] = "SqlServer";
		settings["SecureGate:Provisioning:Targets:default:Server"] = SqlServerAddress;
		settings["SecureGate:Provisioning:Targets:default:AdminConnectionString"] = AdminConnectionString;
	}
}

/// <summary>
/// Provisionamento ponta a ponta contra um SQL Server real (issue #3).
/// </summary>
public sealed class TenantDatabaseProvisioningApiTests(ProvisioningSecureGateApiFactory factory)
	: IClassFixture<ProvisioningSecureGateApiFactory>, IAsyncLifetime
{
	/// <summary>Aplica migrations do banco de plataforma antes do primeiro teste da classe.</summary>
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	/// <inheritdoc />
	public Task DisposeAsync() => Task.CompletedTask;

	private sealed record ProvisioningResponse(bool Applied, string DatabaseName, string LoginName, string? Script);

	private sealed record DatabaseStatus(string Product, bool Reachable, string? FailureReason);

	private sealed record TenantCreated(Guid Id);

	private HttpClient CreateClientWithScopes(params string[] scopes)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateTokenWithScopes(scopes));

		return client;
	}

	private async Task<(HttpClient Client, Guid TenantId, string Slug)> CreateTenantAsync()
	{
		var client = CreateClientWithScopes(SecureGateScopes.Admin);
		// Guid v7 começa com timestamp: fatiar o início daria slugs iguais para tenants criados
		// no mesmo milissegundo. Aqui o que se quer é aleatoriedade, então v4.
		var slug = "prov" + Guid.NewGuid().ToString("N")[..8];

		var response = await client.PostAsJsonAsync("/api/v1/tenants", new { Name = "Tenant " + slug, Slug = slug });
		response.StatusCode.Should().Be(HttpStatusCode.Created);

		var created = await response.Content.ReadFromJsonAsync<TenantCreated>();

		return (client, created!.Id, slug);
	}

	[Fact]
	public async Task Provision_WhenAutomationEnabled_CreatesDatabaseAndAppliesIt()
	{
		var (client, tenantId, _) = await CreateTenantAsync();

		var response = await client.PostAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/databases/logstream/provisioning",
			new { CreateDatabase = true });

		response.StatusCode.Should().Be(HttpStatusCode.OK);

		var result = await response.Content.ReadFromJsonAsync<ProvisioningResponse>();

		result!.Applied.Should().BeTrue();
		result.Script.Should().BeNull("com a automação ligada, o segredo não precisa voltar na resposta");

		// O banco existe de verdade.
		await using var connection = new SqlConnection(factory.AdminConnectionString);
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT DB_ID(@name);";
		command.Parameters.AddWithValue("@name", result.DatabaseName);

		(await command.ExecuteScalarAsync()).Should().NotBe(DBNull.Value);
	}

	[Fact]
	public async Task Provision_NeverReturnsTheConnectionStringAndStoresItEncrypted()
	{
		var (client, tenantId, _) = await CreateTenantAsync();

		var response = await client.PostAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/databases/logstream/provisioning",
			new { CreateDatabase = true });

		var body = await response.Content.ReadAsStringAsync();

		body.Should().NotContain("Password", "a connection string é write-only (ADR-0020)");
		body.Should().NotContain("User Id");

		// E o que foi persistido está cifrado (ADR-0025) — asserção direta na coluna.
		await using var connection = new SqlConnection(factory.GetPlatformConnectionString());
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText =
			"SELECT ds_connection_string FROM tb_tenant_databases WHERE id_fk_tenant = @tenant;";
		command.Parameters.AddWithValue("@tenant", tenantId);

		var stored = (string?)await command.ExecuteScalarAsync();

		stored.Should().StartWith("secco-enc:v1:");
	}

	[Fact]
	public async Task ProvisionedUser_CannotReachAnotherTenantDatabase()
	{
		// O teste que dá sentido à entrega inteira: com SA na connection string, isto passaria
		// e o isolamento da ADR-0005 seria convenção. Com o usuário provisionado, é garantia.
		var (firstClient, firstTenant, _) = await CreateTenantAsync();
		var (_, secondTenant, _) = await CreateTenantAsync();

		var first = await (await firstClient.PostAsJsonAsync(
				$"/api/v1/tenants/{firstTenant}/databases/logstream/provisioning",
				new { CreateDatabase = true }))
			.Content.ReadFromJsonAsync<ProvisioningResponse>();

		var second = await (await firstClient.PostAsJsonAsync(
				$"/api/v1/tenants/{secondTenant}/databases/logstream/provisioning",
				new { CreateDatabase = true }))
			.Content.ReadFromJsonAsync<ProvisioningResponse>();

		// Reconstrói a conexão do PRIMEIRO tenant apontando para o banco do SEGUNDO.
		var builder = new SqlConnectionStringBuilder(factory.AdminConnectionString)
		{
			InitialCatalog = second!.DatabaseName,
			UserID = first!.LoginName,
			Password = await ReadProvisionedPasswordAsync(firstTenant),
			IntegratedSecurity = false,
			TrustServerCertificate = true,
		};

		await using var connection = new SqlConnection(builder.ConnectionString);

		var act = async () => await connection.OpenAsync();

		await act.Should().ThrowAsync<SqlException>(
			"o usuário de um tenant não pode alcançar o banco de outro");
	}

	[Fact]
	public async Task Provision_WhenAlreadyProvisioned_Returns409()
	{
		var (client, tenantId, _) = await CreateTenantAsync();
		var route = $"/api/v1/tenants/{tenantId}/databases/logstream/provisioning";

		var first = await client.PostAsJsonAsync(route, new { CreateDatabase = true });

		// O corpo entra na mensagem de falha: sem isso, um 500 aqui vira um teste vermelho mudo.
		first.StatusCode.Should().Be(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

		(await client.PostAsJsonAsync(route, new { CreateDatabase = true }))
			.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Provision_WhenNameEscapesTheAllowlist_Returns400AndDoesNotEchoTheValue()
	{
		var (client, tenantId, _) = await CreateTenantAsync();

		var response = await client.PostAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/databases/logstream/provisioning",
			new { CreateDatabase = true, DatabaseName = "secco]; DROP DATABASE master--" });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

		// O valor recusado não volta: ele É o vetor de injeção (ADR-0020).
		(await response.Content.ReadAsStringAsync()).Should().NotContain("DROP DATABASE");
	}

	[Fact]
	public async Task Provision_WithoutAdminScope_Returns403()
	{
		var (_, tenantId, _) = await CreateTenantAsync();
		var client = CreateClientWithScopes("catalog:logstream");

		var response = await client.PostAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/databases/logstream/provisioning",
			new { CreateDatabase = true });

		response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
	}

	[Fact]
	public async Task Status_AfterProvisioning_ReportsTheDatabaseAsReachable()
	{
		var (client, tenantId, _) = await CreateTenantAsync();

		await client.PostAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/databases/logstream/provisioning",
			new { CreateDatabase = true });

		var statuses = await client.GetFromJsonAsync<List<DatabaseStatus>>(
			$"/api/v1/tenants/{tenantId}/databases/status");

		statuses.Should().ContainSingle()
			.Which.Should().Match<DatabaseStatus>(status => status.Product == "logstream" && status.Reachable);
	}

	[Fact]
	public async Task Status_WhenDatabaseIsUnreachable_ReportsWithoutLeakingTheConnectionString()
	{
		var (client, tenantId, _) = await CreateTenantAsync();

		// Cadastra à mão um banco que não existe — o caminho do PUT da 6.3.
		await client.PutAsJsonAsync(
			$"/api/v1/tenants/{tenantId}/databases/logstream",
			new { ConnectionString = $"Server={factory.SqlServerAddress};Database=nao_existe_{Guid.NewGuid():N};User Id=fantasma;Password=IrrelevanteMasLonga123;TrustServerCertificate=True" });

		var response = await client.GetAsync($"/api/v1/tenants/{tenantId}/databases/status");
		var body = await response.Content.ReadAsStringAsync();

		body.Should().NotContain("Password");
		body.Should().NotContain("fantasma");

		var statuses = await response.Content.ReadFromJsonAsync<List<DatabaseStatus>>();

		statuses.Should().ContainSingle().Which.Reachable.Should().BeFalse();
	}

	/// <summary>Lê a senha provisionada decifrando o catálogo pelo próprio contexto da API.</summary>
	/// <param name="tenantId">Tenant cujo banco foi provisionado.</param>
	private async Task<string> ReadProvisionedPasswordAsync(Guid tenantId)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();

		var database = await context.TenantDatabases.FirstAsync(entry => entry.TenantId == tenantId);

		return new SqlConnectionStringBuilder(database.ConnectionString).Password;
	}
}
