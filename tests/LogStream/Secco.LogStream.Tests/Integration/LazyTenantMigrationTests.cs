using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Secco.SDK.Testing;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

/// <summary>
/// Tenant provisionado DEPOIS do deploy (ADR-0028): banco vazio, ninguém rodou migrate — o primeiro
/// uso cria o schema, sem reiniciar o produto (ADR-0038).
/// </summary>
public sealed class LazyTenantFactory : SeccoApiFactory<Program>
{
	public const string DatabaseName = "secco_logstream_lazy";

	protected override string Audience => "secco-logstream";

	public Guid TenantNovo { get; } = Guid.NewGuid();

	// De propósito: NENHUMA migração prévia — é o cenário do tenant novo
	protected override Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;

	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		AddTenant(settings, TenantNovo, GetConnectionStringFor(DatabaseName));
		AddRolePermissions(settings, DefaultTestRole, "log-entries:read", "log-entries:write");
	}

	// Banco existente e vazio — como o provisionamento da ADR-0028 entrega
	protected override Task OnInitializedAsync() => CreateDatabaseAsync(DatabaseName);
}

public class LazyTenantMigrationTests(LazyTenantFactory factory) : IClassFixture<LazyTenantFactory>
{
	[Fact]
	public async Task PrimeiroUso_TenantComBancoVazio_CriaSchemaEResponde()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(factory.TenantNovo));

		// Nenhuma recursão: o contexto de migração é criado SEM o interceptor. Com a recursão, a
		// migração esperaria por si mesma e este GET não voltaria.
		var response = await client.GetAsync(new Uri("/api/v1/log-entries", UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);

		await using var connection = new SqlConnection(factory.GetConnectionStringFor(LazyTenantFactory.DatabaseName));
		await connection.OpenAsync();
		await using var command = new SqlCommand("SELECT COUNT(*) FROM __EFMigrationsHistory", connection);
		((int)(await command.ExecuteScalarAsync())!).Should().BeGreaterThan(0);
	}
}
