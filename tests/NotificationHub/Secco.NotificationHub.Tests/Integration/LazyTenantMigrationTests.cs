using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Tenant provisionado DEPOIS do deploy (ADR-0028): banco vazio, ninguém rodou migrate — o primeiro
/// uso cria o schema, sem reiniciar o produto (ADR-0038). Usa o <c>TenantNovo</c> da factory
/// compartilhada, que <c>MigrateAsync</c> deixa de fora de propósito.
/// </summary>
[Collection(NotificationHubApiCollectionDefinition.Name)]
public class LazyTenantMigrationTests(NotificationHubApiFactory factory)
{
	[Fact]
	public async Task PrimeiroUso_TenantComBancoVazio_CriaSchemaEResponde()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(factory.TenantNovo));

		// Nenhuma recursão: o contexto de migração é criado SEM o interceptor. Com a recursão, a
		// migração esperaria por si mesma e este GET não voltaria.
		var response = await client.GetAsync(new Uri($"/api/v1/in-app-notifications?userId={Guid.NewGuid()}", UriKind.Relative));

		response.StatusCode.Should().Be(HttpStatusCode.OK);

		await using var connection = new SqlConnection(factory.GetConnectionStringFor(NotificationHubApiFactory.NewTenantDatabaseName));
		await connection.OpenAsync();
		await using var command = new SqlCommand("SELECT COUNT(*) FROM __EFMigrationsHistory", connection);
		((int)(await command.ExecuteScalarAsync())!).Should().BeGreaterThan(0);
	}
}
