using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Secco.SecureGate.Infrastructure;
using Secco.SecureGate.Migrations.SqlServer;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O helper <see cref="TenantRowLevelSecurity"/> é a fonte única do SQL do RLS (ADR-0041): a migration
/// o chama e migrations futuras o usam para soltar a politica antes de alterar colunas (SCHEMABINDING)
/// e recria-la depois. Aqui se prova que o que o <c>Create</c> produz e exatamente o que a migration aplicou.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class TenantRowLevelSecurityHelperTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly string[] ProtectedTables =
	[
		"tb_roles", "tb_role_claims", "tb_user_roles", "tb_tenant_memberships", "tb_users",
		"tb_tenant_databases", "tb_tenant_federations", "tb_tenant_group_role_mappings", "tb_elevation_grants",
		"tb_tenant_admin_grants", "tb_authority_audit_entries",
	];

	private static readonly string[] PredicateKinds = ["BLOCK|AFTER INSERT", "BLOCK|AFTER UPDATE", "FILTER|NULL"];

	// Pessoa: escrita so pelo tenant dono (ADR-0040), inclusive BEFORE UPDATE e BEFORE DELETE
	private static readonly string[] UserPredicateKinds =
	[
		"BLOCK|AFTER INSERT", "BLOCK|AFTER UPDATE", "BLOCK|BEFORE DELETE", "BLOCK|BEFORE UPDATE", "FILTER|NULL",
	];

	public Task InitializeAsync() => factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task Migration_AplicaFilterEBlockInsertEUpdate_EmTodasAsTabelasProtegidas()
	{
		var predicates = await PredicatesAsync(factory.GetPlatformConnectionString());

		predicates.Should().Equal(Expected());
	}

	[Fact]
	public async Task DropECreate_ReproduzemExatamenteOEstadoDaMigration()
	{
		await factory.CreateDatabaseAsync("secco_rls_helper");
		var connectionString = factory.GetConnectionStringFor("secco_rls_helper");

		await using var context = PlatformContextTestFactory.Create(SecureGateDatabaseProviderConfigurator.CreateOptions(
			SecureGateDatabaseProvider.SqlServer, connectionString));
		await context.GetService<IMigrator>().MigrateAsync();

		(await PredicatesAsync(connectionString)).Should().Equal(Expected());

		var drop = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
		TenantRowLevelSecurity.Drop(drop);
		await RunAsync(connectionString, drop);
		(await PredicatesAsync(connectionString)).Should().BeEmpty("Drop remove a politica inteira");

		// Idempotente: soltar de novo nao falha
		await RunAsync(connectionString, drop);

		var create = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
		TenantRowLevelSecurity.Create(create);
		await RunAsync(connectionString, create);
		(await PredicatesAsync(connectionString)).Should().Equal(Expected());
	}

	/// <summary>Por tabela: 1 FILTER, 1 BLOCK AFTER INSERT e 1 BLOCK AFTER UPDATE (tb_users: mais BEFORE UPDATE e BEFORE DELETE).</summary>
	private static List<string> Expected() =>
		ProtectedTables
			.Order(StringComparer.Ordinal)
			.SelectMany(table => (table == "tb_users" ? UserPredicateKinds : PredicateKinds).Select(kind => $"{table}|{kind}"))
			.ToList();

	private static async Task<List<string>> PredicatesAsync(string connectionString)
	{
		await using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();
		await using var command = connection.CreateCommand();
		command.CommandText =
			"SELECT OBJECT_NAME(p.target_object_id), p.predicate_type_desc, ISNULL(p.operation_desc, N'NULL') "
			+ "FROM sys.security_predicates AS p JOIN sys.security_policies AS pol ON pol.object_id = p.object_id "
			+ "WHERE pol.name = N'pol_tenant_isolation' "
			+ "ORDER BY 1, 2, 3";

		var rows = new List<string>();
		await using var reader = await command.ExecuteReaderAsync();
		while (await reader.ReadAsync())
		{
			rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}|{reader.GetString(2)}");
		}

		// Ordenacao ordinal para comparar com Expected()
		return rows.Order(StringComparer.Ordinal).ToList();
	}

	private static async Task RunAsync(string connectionString, MigrationBuilder builder)
	{
		await using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		foreach (var operation in builder.Operations.OfType<SqlOperation>())
		{
			await using var command = connection.CreateCommand();
			command.CommandText = operation.Sql;
			await command.ExecuteNonQueryAsync();
		}
	}
}
