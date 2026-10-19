using System.Data;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Domain.Elevation;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Row-Level Security do banco de plataforma (ADR-0041, camada 2). Aqui o filtro do EF NÃO participa:
/// SQL cru, por uma conexão com o login de RUNTIME (sem posse), com o contexto de sessão gravado à mão
/// como o <c>TenantSessionContextInterceptor</c> o grava. O que se prova é o BANCO segurando sozinho.
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class RowLevelSecurityTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private const string Product = "rls";

	private Seed _s = null!;

	private sealed record Seed(
		Guid TenantA, Guid TenantB, Guid TenantC, Guid RoleA, Guid RoleB, Guid Person, Guid OwnerOfB, Guid WithoutGrant);

	/// <summary>Tabela protegida + como reconhecer, por SQL, uma linha do tenant A e uma do tenant B.</summary>
	private sealed record Protected(string Table, string RowOfA, string RowOfB);

	private static readonly Protected[] Tables =
	[
		new("tb_roles", "id_fk_tenant = @a", "id_fk_tenant = @b"),
		new("tb_role_claims", "id_fk_role = @roleA", "id_fk_role = @roleB"),
		new("tb_user_roles", "id_pfk_role = @roleA", "id_pfk_role = @roleB"),
		new("tb_tenant_memberships", "id_pfk_tenant = @a", "id_pfk_tenant = @b"),
		// A pessoa é dona em A e membro em B; o dono de B só existe em B
		new("tb_users", "id_pk_user = @person", "id_pk_user = @ownerOfB"),
		new("tb_tenant_databases", "id_fk_tenant = @a", "id_fk_tenant = @b"),
		new("tb_tenant_federations", "id_fk_tenant = @a", "id_fk_tenant = @b"),
		new("tb_tenant_group_role_mappings", "id_fk_tenant = @a", "id_fk_tenant = @b"),
		new("tb_elevation_grants", "id_fk_tenant = @a", "id_fk_tenant = @b"),
		new("tb_tenant_admin_grants", "id_fk_tenant = @a", "id_fk_tenant = @b"),
	];

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();

		var tenantA = await IdentitySeed.TenantAsync(factory);
		var tenantB = await IdentitySeed.TenantAsync(factory);
		var tenantC = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, tenantA, "perfil-a", "logs:read");
		await IdentitySeed.RoleAsync(factory, tenantB, "perfil-b", "logs:write");
		var person = await IdentitySeed.UserAsync(factory, tenantA, $"rls-pessoa-{Guid.NewGuid():N}@teste.local", "perfil-a");
		var ownerOfB = await IdentitySeed.UserAsync(factory, tenantB, $"rls-dono-b-{Guid.NewGuid():N}@teste.local", "perfil-b");

		// Pessoa sem concessao de elevacao (unica por usuario): alvo do INSERT bloqueado em tb_elevation_grants
		var withoutGrant = await IdentitySeed.UserAsync(factory, tenantC, $"rls-sem-concessao-{Guid.NewGuid():N}@teste.local");

		using var scope = factory.Services.CreatePlatformScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		var roleA = await context.Roles.SingleAsync(r => r.TenantId == tenantA && r.NormalizedName == "PERFIL-A");
		var roleB = await context.Roles.SingleAsync(r => r.TenantId == tenantB && r.NormalizedName == "PERFIL-B");

		context.TenantMemberships.AddRange(
			new TenantMembership(person, tenantA, DateTimeOffset.UtcNow),
			new TenantMembership(person, tenantB, DateTimeOffset.UtcNow),
			new TenantMembership(ownerOfB, tenantB, DateTimeOffset.UtcNow));
		context.TenantDatabases.AddRange(
			new TenantDatabase(tenantA, Product, "Server=a;Database=a"),
			new TenantDatabase(tenantB, Product, "Server=b;Database=b"));
		context.TenantFederations.AddRange(
			new TenantFederation(tenantA, Guid.NewGuid()),
			new TenantFederation(tenantB, Guid.NewGuid()));
		context.TenantGroupRoleMappings.AddRange(
			new TenantGroupRoleMapping(tenantA, Guid.NewGuid(), "Grupo A", roleA.Id),
			new TenantGroupRoleMapping(tenantB, Guid.NewGuid(), "Grupo B", roleB.Id));
		context.ElevationGrants.AddRange(
			new ElevationGrant(person, tenantA, "teste", null, DateTimeOffset.UtcNow),
			new ElevationGrant(ownerOfB, tenantB, "teste", null, DateTimeOffset.UtcNow));
		context.TenantAdminGrants.AddRange(
			new TenantAdminGrant(person, tenantA, "teste", DateTimeOffset.UtcNow),
			new TenantAdminGrant(ownerOfB, tenantB, "teste", DateTimeOffset.UtcNow));
		await context.SaveChangesAsync();

		_s = new Seed(tenantA, tenantB, tenantC, roleA.Id, roleB.Id, person, ownerOfB, withoutGrant);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	// ---- Infra de SQL cru ----

	private static async Task<SqlConnection> OpenAsync(string connectionString, string? mode, Guid? tenant = null)
	{
		var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();

		if (mode is not null)
		{
			await ExecuteAsync(connection, "EXEC sp_set_session_context @key = N'secco_mode', @value = @v, @read_only = 1;", ("@v", mode));
		}

		if (tenant is { } id)
		{
			await ExecuteAsync(connection, "EXEC sp_set_session_context @key = N'secco_tenant', @value = @v, @read_only = 1;", ("@v", id.ToString("D")));
		}

		return connection;
	}

	/// <summary>Conexão do login de runtime com o contexto gravado (nulo = sem contexto).</summary>
	private Task<SqlConnection> RuntimeAsync(string? mode, Guid? tenant = null) =>
		OpenAsync(factory.GetRuntimeConnectionString(), mode, tenant);

	private static async Task<int> ExecuteAsync(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
	{
		await using var command = Command(connection, sql, parameters);

		return await command.ExecuteNonQueryAsync();
	}

	private static SqlCommand Command(SqlConnection connection, string sql, (string Name, object Value)[] parameters)
	{
		var command = connection.CreateCommand();
		command.CommandText = sql;

		foreach (var (name, value) in parameters)
		{
			command.Parameters.AddWithValue(name, value);
		}

		return command;
	}

	private async Task<int> CountAsync(SqlConnection connection, string table, string where)
	{
		// Tabela e filtro vêm da lista fixa acima — nunca de input
		await using var command = Command(
			connection,
			$"SELECT COUNT(*) FROM {table} WHERE {where}",
			[
				("@a", _s.TenantA), ("@b", _s.TenantB), ("@roleA", _s.RoleA), ("@roleB", _s.RoleB),
				("@person", _s.Person), ("@ownerOfB", _s.OwnerOfB),
			]);

		return (int)(await command.ExecuteScalarAsync())!;
	}

	private static async Task<int> TotalAsync(SqlConnection connection, string table)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = $"SELECT COUNT(*) FROM {table}";

		return (int)(await command.ExecuteScalarAsync())!;
	}

	private static async Task<string?> SessionValueAsync(SqlConnection connection, string key)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = "SELECT CAST(SESSION_CONTEXT(@key) AS nvarchar(64))";
		command.Parameters.AddWithValue("@key", key);

		return await command.ExecuteScalarAsync() as string;
	}

	// ---- SQL cru nas tabelas protegidas ----

	[Fact]
	public async Task EscopoDeA_EmCadaTabela_VeSoLinhasDeA()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);

		foreach (var table in Tables)
		{
			(await CountAsync(connection, table.Table, table.RowOfA)).Should().BeGreaterThan(0, $"{table.Table}: o tenant A vê o que é dele");
			(await CountAsync(connection, table.Table, table.RowOfB)).Should().Be(0, $"{table.Table}: o tenant A não vê linha só de B");
		}
	}

	[Fact]
	public async Task EscopoDeB_EmCadaTabela_VeSoLinhasDeB()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantB);

		foreach (var table in Tables)
		{
			(await CountAsync(connection, table.Table, table.RowOfB)).Should().BeGreaterThan(0, $"{table.Table}: o tenant B vê o que é dele");
		}

		(await CountAsync(connection, "tb_roles", "id_fk_tenant = @a")).Should().Be(0);
		(await CountAsync(connection, "tb_role_claims", "id_fk_role = @roleA")).Should().Be(0);
		(await CountAsync(connection, "tb_user_roles", "id_pfk_role = @roleA")).Should().Be(0);
		(await CountAsync(connection, "tb_tenant_memberships", "id_pfk_tenant = @a")).Should().Be(0);
		(await CountAsync(connection, "tb_tenant_databases", "id_fk_tenant = @a")).Should().Be(0);
	}

	[Fact]
	public async Task Pessoa_DonaEmAMembroEmB_VistaPelosDois_EDonoDeBSoPorB()
	{
		await using var inB = await RuntimeAsync("tenant", _s.TenantB);
		(await CountAsync(inB, "tb_users", "id_pk_user = @person")).Should().Be(1, "membro em B");
		(await CountAsync(inB, "tb_users", "id_pk_user = @ownerOfB")).Should().Be(1);

		await using var inC = await RuntimeAsync("tenant", _s.TenantC);
		(await CountAsync(inC, "tb_users", "id_pk_user = @person OR id_pk_user = @ownerOfB")).Should().Be(0, "nem dono nem membro em C");
	}

	[Fact]
	public async Task SemContexto_EmCadaTabela_NaoMostraNada()
	{
		await using var connection = await RuntimeAsync(mode: null);

		foreach (var table in Tables)
		{
			(await TotalAsync(connection, table.Table)).Should().Be(0, $"{table.Table}: sem contexto de sessão nada aparece (fail-closed)");
		}
	}

	[Fact]
	public async Task ModoInvalido_NaoMostraNada()
	{
		await using var connection = await RuntimeAsync("qualquer-coisa", _s.TenantA);

		foreach (var table in Tables)
		{
			(await TotalAsync(connection, table.Table)).Should().Be(0);
		}
	}

	[Fact]
	public async Task TenantMalformado_NaoMostraNada()
	{
		await using var connection = await RuntimeAsync("tenant");
		await ExecuteAsync(connection, "EXEC sp_set_session_context @key = N'secco_tenant', @value = N'isto-nao-e-guid', @read_only = 1;");

		foreach (var table in Tables)
		{
			(await TotalAsync(connection, table.Table)).Should().Be(0);
		}
	}

	[Fact]
	public async Task ModoPlataforma_EmCadaTabela_VeOsDoisTenants()
	{
		await using var connection = await RuntimeAsync("platform");

		foreach (var table in Tables)
		{
			(await CountAsync(connection, table.Table, table.RowOfA)).Should().BeGreaterThan(0, table.Table);
			(await CountAsync(connection, table.Table, table.RowOfB)).Should().BeGreaterThan(0, table.Table);
		}
	}

	// ---- Escrita bloqueada pelo banco ----

	private const string InsertMembership =
		"INSERT INTO tb_tenant_memberships (id_pfk_user, id_pfk_tenant, fl_active, dt_created_at) VALUES (@u, @t, 1, SYSDATETIMEOFFSET())";

	[Fact]
	public async Task Insert_DeLinhaDeOutroTenant_EBloqueadoPeloBanco()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);

		var act = () => ExecuteAsync(connection, InsertMembership, ("@u", _s.OwnerOfB), ("@t", _s.TenantC));

		// 33504: "The attempted operation failed because the target object has a block predicate that conflicts with this operation"
		(await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(33504);
	}

	[Fact]
	public async Task Insert_DeLinhaDoProprioTenant_Passa()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantC);
		await using var transaction = connection.BeginTransaction();

		await using var command = connection.CreateCommand();
		command.Transaction = transaction;
		command.CommandText = InsertMembership;
		command.Parameters.AddWithValue("@u", _s.OwnerOfB);
		command.Parameters.AddWithValue("@t", _s.TenantC);

		(await command.ExecuteNonQueryAsync()).Should().Be(1);
		transaction.Rollback();
	}

	[Fact]
	public async Task Update_QueMoveLinhaParaOutroTenant_EBloqueadoPeloBanco()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);

		var act = () => ExecuteAsync(
			connection,
			"UPDATE tb_tenant_memberships SET id_pfk_tenant = @c WHERE id_pfk_user = @u AND id_pfk_tenant = @a",
			("@c", _s.TenantC), ("@u", _s.Person), ("@a", _s.TenantA));

		(await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(33504);
	}

	[Fact]
	public async Task UpdateEDelete_DeLinhaDeOutroTenant_NaoAcertamNada()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);

		(await ExecuteAsync(connection, "UPDATE tb_tenant_databases SET ds_product = ds_product WHERE id_fk_tenant = @b", ("@b", _s.TenantB)))
			.Should().Be(0, "a linha de B é invisível para A: o filtro vale para UPDATE");
		(await ExecuteAsync(connection, "DELETE FROM tb_tenant_databases WHERE id_fk_tenant = @b", ("@b", _s.TenantB)))
			.Should().Be(0, "e para DELETE");

		await using var platform = await RuntimeAsync("platform");
		(await CountAsync(platform, "tb_tenant_databases", "id_fk_tenant = @b")).Should().Be(1);
	}

	[Fact]
	public async Task Insert_DePermissaoEAtribuicaoNoPerfilDeOutroTenant_EBloqueadoPeloBanco()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);

		var claim = () => ExecuteAsync(
			connection,
			"INSERT INTO tb_role_claims (id_fk_role, ds_claim_type, ds_claim_value) VALUES (@roleB, N'permission', N'x:y')",
			("@roleB", _s.RoleB));
		(await claim.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(33504);

		var assignment = () => ExecuteAsync(
			connection,
			"INSERT INTO tb_user_roles (id_pfk_user, id_pfk_role, ie_origin) VALUES (@u, @roleB, 0)",
			("@u", _s.Person), ("@roleB", _s.RoleB));
		(await assignment.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(33504);
	}

	[Fact]
	public async Task Update_QueMovePessoaParaForaDoEscopo_EBloqueadoPeloBanco()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);
		await using var transaction = connection.BeginTransaction();

		// Enquanto a pessoa for membro de A, mudar o dono para C ainda a deixa visivel em A (permitido).
		// Sem o vinculo com A, a mesma mudanca a tiraria do escopo corrente: o BLOCK recusa.
		await using var removeMembership = connection.CreateCommand();
		removeMembership.Transaction = transaction;
		removeMembership.CommandText = "DELETE FROM tb_tenant_memberships WHERE id_pfk_user = @person AND id_pfk_tenant = @a";
		removeMembership.Parameters.AddWithValue("@person", _s.Person);
		removeMembership.Parameters.AddWithValue("@a", _s.TenantA);
		(await removeMembership.ExecuteNonQueryAsync()).Should().Be(1);

		await using var move = connection.CreateCommand();
		move.Transaction = transaction;
		move.CommandText = "UPDATE tb_users SET id_fk_tenant = @c WHERE id_pk_user = @person";
		move.Parameters.AddWithValue("@c", _s.TenantC);
		move.Parameters.AddWithValue("@person", _s.Person);

		// Dono A ainda vale (a pessoa continua dona em A ate o UPDATE); depois do UPDATE a linha sairia do escopo
		var act = () => move.ExecuteNonQueryAsync();
		(await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(33504);

		transaction.Rollback();
	}

	// ---- BLOCK por tabela: cada predicado de escrita tem a sua prova ----

	/// <summary>
	/// INSERT de uma linha de OUTRO tenant, valido em tudo menos no tenant (para o erro ser o do BLOCK, nao de
	/// NOT NULL/FK). Executado em escopo A; B e C sao os "outros".
	/// </summary>
	private static readonly Dictionary<string, string> ForeignInserts = new(StringComparer.Ordinal)
	{
		["tb_roles"] = "INSERT INTO tb_roles (id_pk_role, id_fk_tenant, ds_name, ds_normalized_name) VALUES (NEWID(), @b, N'intruso', N'INTRUSO')",
		["tb_role_claims"] = "INSERT INTO tb_role_claims (id_fk_role, ds_claim_type, ds_claim_value) VALUES (@roleB, N'permission', N'x:y')",
		["tb_user_roles"] = "INSERT INTO tb_user_roles (id_pfk_user, id_pfk_role, ie_origin) VALUES (@person, @roleB, 0)",
		["tb_tenant_memberships"] = "INSERT INTO tb_tenant_memberships (id_pfk_user, id_pfk_tenant, fl_active, dt_created_at) VALUES (@ownerOfB, @c, 1, SYSDATETIMEOFFSET())",
		["tb_users"] = "INSERT INTO tb_users (id_pk_user, id_fk_tenant, fl_email_confirmed, fl_phone_number_confirmed, fl_two_factor_enabled, fl_lockout_enabled, nr_access_failed_count, ds_email, ds_normalized_email, ds_user_name, ds_normalized_user_name) "
			+ "VALUES (NEWID(), @b, 0, 0, 0, 0, 0, N'intruso@teste.local', N'INTRUSO@TESTE.LOCAL', N'intruso@teste.local', N'INTRUSO@TESTE.LOCAL')",
		["tb_tenant_databases"] = "INSERT INTO tb_tenant_databases (id_pk_tenant_database, id_fk_tenant, ds_product, ds_connection_string, dt_created_at, dt_updated_at) VALUES (NEWID(), @b, N'intruso', N'x', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
		["tb_tenant_federations"] = "INSERT INTO tb_tenant_federations (id_pk_tenant_federation, id_fk_tenant, ds_provider, directory_id, fl_enabled, dt_created_at, dt_updated_at) VALUES (NEWID(), @c, N'entra', NEWID(), 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
		["tb_tenant_group_role_mappings"] = "INSERT INTO tb_tenant_group_role_mappings (id_pk_tenant_group_role_mapping, id_fk_tenant, entra_group_id, ds_entra_group_display_name, id_fk_role, dt_created_at) VALUES (NEWID(), @b, NEWID(), N'intruso', @roleB, SYSDATETIMEOFFSET())",
		["tb_elevation_grants"] = "INSERT INTO tb_elevation_grants (id_pk_elevation_grant, id_fk_user, id_fk_tenant, ds_granted_by, dt_created_at) VALUES (NEWID(), @withoutGrant, @c, N'teste', SYSDATETIMEOFFSET())",
		["tb_tenant_admin_grants"] = "INSERT INTO tb_tenant_admin_grants (id_pk_tenant_admin_grant, id_fk_user, id_fk_tenant, ds_granted_by, dt_created_at) VALUES (NEWID(), @person, @c, N'teste', SYSDATETIMEOFFSET())",
	};

	/// <summary>UPDATE em escopo A que move uma linha DE A para fora do escopo (tenant C ou perfil de B).</summary>
	private static readonly Dictionary<string, string> MovingUpdates = new(StringComparer.Ordinal)
	{
		["tb_roles"] = "UPDATE tb_roles SET id_fk_tenant = @c WHERE id_fk_tenant = @a",
		["tb_role_claims"] = "UPDATE tb_role_claims SET id_fk_role = @roleB WHERE id_fk_role = @roleA",
		["tb_user_roles"] = "UPDATE tb_user_roles SET id_pfk_role = @roleB WHERE id_pfk_role = @roleA",
		["tb_tenant_memberships"] = "UPDATE tb_tenant_memberships SET id_pfk_tenant = @c WHERE id_pfk_tenant = @a AND id_pfk_user = @person",
		["tb_users"] = "UPDATE tb_users SET id_fk_tenant = @c WHERE id_pk_user = @person",
		["tb_tenant_databases"] = "UPDATE tb_tenant_databases SET id_fk_tenant = @c WHERE id_fk_tenant = @a",
		["tb_tenant_federations"] = "UPDATE tb_tenant_federations SET id_fk_tenant = @c WHERE id_fk_tenant = @a",
		["tb_tenant_group_role_mappings"] = "UPDATE tb_tenant_group_role_mappings SET id_fk_tenant = @c WHERE id_fk_tenant = @a",
		["tb_elevation_grants"] = "UPDATE tb_elevation_grants SET id_fk_tenant = @c WHERE id_fk_tenant = @a",
		["tb_tenant_admin_grants"] = "UPDATE tb_tenant_admin_grants SET id_fk_tenant = @c WHERE id_fk_tenant = @a",
	};

	public static TheoryData<string> ProtectedTableNames => new(Tables.Select(t => t.Table));

	private async Task ExpectBlockedAsync(string sql)
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);
		await using var transaction = connection.BeginTransaction();

		try
		{
			await using var command = Command(
				connection,
				sql,
				[("@a", _s.TenantA), ("@b", _s.TenantB), ("@c", _s.TenantC), ("@roleA", _s.RoleA), ("@roleB", _s.RoleB), ("@person", _s.Person), ("@ownerOfB", _s.OwnerOfB), ("@withoutGrant", _s.WithoutGrant)]);
			command.Transaction = transaction;

			var act = () => command.ExecuteNonQueryAsync();
			(await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(33504, "o BLOCK PREDICATE do banco recusa");
		}
		finally
		{
			transaction.Rollback();
		}
	}

	[Theory]
	[MemberData(nameof(ProtectedTableNames))]
	public Task Insert_DeLinhaDeOutroTenant_EBloqueadoEmCadaTabela(string table) => ExpectBlockedAsync(ForeignInserts[table]);

	[Theory]
	[MemberData(nameof(ProtectedTableNames))]
	public Task Update_QueMoveLinhaParaForaDoEscopo_EBloqueadoEmCadaTabela(string table) => ExpectBlockedAsync(MovingUpdates[table]);

	// ---- tb_users: escrita so pelo tenant dono (ADR-0040) ----

	[Fact]
	public async Task Users_EscritaPeloTenantOndeAPessoaESoMembro_EBloqueada()
	{
		// A pessoa e dona em A e membro em B. Em B ela e visivel (FILTER) mas nao e alteravel nem removivel.
		await using var connection = await RuntimeAsync("tenant", _s.TenantB);
		(await CountAsync(connection, "tb_users", "id_pk_user = @person")).Should().Be(1, "membro enxerga");

		await using var transaction = connection.BeginTransaction();

		try
		{

			var update = () => ExecuteInAsync(connection, transaction, "UPDATE tb_users SET fl_lockout_enabled = fl_lockout_enabled WHERE id_pk_user = @person");
			(await update.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(33504);

			var delete = () => ExecuteInAsync(connection, transaction, "DELETE FROM tb_users WHERE id_pk_user = @person");
			(await delete.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(33504);
		}
		finally
		{
			transaction.Rollback();
		}
	}

	[Theory]
	[InlineData("tenant")]
	[InlineData("platform")]
	public async Task Users_EscritaPeloTenantDonoEPelaPlataforma_Passa(string mode)
	{
		await using var connection = await RuntimeAsync(mode, mode == "tenant" ? _s.TenantA : null);
		await using var transaction = connection.BeginTransaction();

		try
		{
			(await ExecuteInAsync(connection, transaction, "UPDATE tb_users SET fl_lockout_enabled = fl_lockout_enabled WHERE id_pk_user = @person"))
				.Should().Be(1);
		}
		finally
		{
			transaction.Rollback();
		}
	}

	private async Task<int> ExecuteInAsync(SqlConnection connection, SqlTransaction transaction, string sql)
	{
		await using var command = Command(connection, sql, [("@person", _s.Person)]);
		command.Transaction = transaction;

		return await command.ExecuteNonQueryAsync();
	}

	// ---- Contexto somente leitura ----

	[Fact]
	public async Task Contexto_NaoPodeSerRegravadoNaMesmaConexao()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);

		var escalate = () => ExecuteAsync(connection, "EXEC sp_set_session_context @key = N'secco_mode', @value = N'platform';");
		(await escalate.Should().ThrowAsync<SqlException>()).WithMessage("*read_only*");

		var hop = () => ExecuteAsync(connection, "EXEC sp_set_session_context @key = N'secco_tenant', @value = @v;", ("@v", _s.TenantB.ToString("D")));
		await hop.Should().ThrowAsync<SqlException>();

		var rewriteReadOnly = () => ExecuteAsync(connection, "EXEC sp_set_session_context @key = N'secco_mode', @value = N'platform', @read_only = 1;");
		await rewriteReadOnly.Should().ThrowAsync<SqlException>();

		(await SessionValueAsync(connection, "secco_mode")).Should().Be("tenant");
		(await CountAsync(connection, "tb_roles", "id_fk_tenant = @b")).Should().Be(0, "a leitura seguinte continua restrita a A");
	}

	[Fact]
	public async Task RuntimeSemPosse_NaoDesligaNemAlteraAPolitica()
	{
		await using var connection = await RuntimeAsync("tenant", _s.TenantA);

		var off = () => ExecuteAsync(connection, "ALTER SECURITY POLICY dbo.pol_tenant_isolation WITH (STATE = OFF);");
		await off.Should().ThrowAsync<SqlException>();

		var drop = () => ExecuteAsync(connection, "DROP SECURITY POLICY dbo.pol_tenant_isolation;");
		await drop.Should().ThrowAsync<SqlException>();
	}

	[Fact]
	public async Task ConexaoDoPool_ReaproveitadaEntreTenants_NaoHerdaOContexto()
	{
		// Mesma string, mesmo pool: sp_reset_connection ao devolver/reaproveitar limpa o contexto, inclusive o somente leitura
		for (var round = 0; round < 3; round++)
		{
			await using (var a = await RuntimeAsync("tenant", _s.TenantA))
			{
				(await CountAsync(a, "tb_roles", "id_fk_tenant = @b")).Should().Be(0);
				(await CountAsync(a, "tb_roles", "id_fk_tenant = @a")).Should().BeGreaterThan(0);
			}

			await using (var b = await RuntimeAsync("tenant", _s.TenantB))
			{
				(await CountAsync(b, "tb_roles", "id_fk_tenant = @a")).Should().Be(0);
				(await CountAsync(b, "tb_roles", "id_fk_tenant = @b")).Should().BeGreaterThan(0);
			}

			await using (var none = await RuntimeAsync(mode: null))
			{
				(await SessionValueAsync(none, "secco_mode")).Should().BeNull("o contexto da conexão anterior não sobrevive ao pool");
				(await TotalAsync(none, "tb_roles")).Should().Be(0);
			}
		}
	}

	// ---- Dono e sysadmin: o RLS também vale para eles ----

	[Fact]
	public async Task Sysadmin_SemContexto_NaoVeLinhaDeTenant_EComContextoDePlataformaVe()
	{
		await using (var sa = await OpenAsync(factory.GetPlatformConnectionString(), mode: null))
		{
			(await TotalAsync(sa, "tb_roles")).Should().Be(0, "a política vale também para sysadmin/dbo");
			(await TotalAsync(sa, "tb_users")).Should().Be(0);
		}

		await using (var sa = await OpenAsync(factory.GetPlatformConnectionString(), "platform"))
		{
			(await CountAsync(sa, "tb_roles", "id_fk_tenant = @a")).Should().BeGreaterThan(0);
		}
	}

	[Fact]
	public async Task DbOwner_NaoSysadmin_SemContexto_NaoVeLinhaDeTenant()
	{
		var login = $"secco_rls_owner_{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";
		var password = $"Ow{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}a1!";

		await using (var admin = await OpenAsync(factory.GetPlatformConnectionString(), mode: null))
		{
			await ExecuteAsync(
				admin,
				"DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = OFF;' "
				+ "+ N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';' "
				+ "+ N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@login) + N';'; EXEC sp_executesql @sql;",
				("@login", login), ("@password", password));
		}

		try
		{
			var builder = new SqlConnectionStringBuilder(factory.GetPlatformConnectionString())
			{
				UserID = login,
				Password = password,
				IntegratedSecurity = false,
				Pooling = false,
			};

			await using var owner = await OpenAsync(builder.ConnectionString, mode: null);
			await using var check = owner.CreateCommand();
			check.CommandText = "SELECT IS_ROLEMEMBER(N'db_owner'), IS_SRVROLEMEMBER(N'sysadmin')";
			await using (var reader = await check.ExecuteReaderAsync())
			{
				(await reader.ReadAsync()).Should().BeTrue();
				reader.GetInt32(0).Should().Be(1);
				reader.GetInt32(1).Should().Be(0);
			}

			(await TotalAsync(owner, "tb_roles")).Should().Be(0, "a política vale também para db_owner");
		}
		finally
		{
			await using var admin = await OpenAsync(factory.GetPlatformConnectionString(), mode: null);
			await ExecuteAsync(
				admin,
				"DECLARE @sql nvarchar(max) = N'DROP USER ' + QUOTENAME(@login) + N'; DROP LOGIN ' + QUOTENAME(@login) + N';'; EXEC sp_executesql @sql;",
				("@login", login));
		}
	}

	// ---- Down da migration ----

	[Fact]
	public async Task Migration_Down_RemovePoliticaEFuncoes_EUpRecria()
	{
		const string previous = "20261015210155_AddTenantMemberships";
		await factory.CreateDatabaseAsync("secco_rls_down");
		var connectionString = factory.GetConnectionStringFor("secco_rls_down");

		await using var context = PlatformContextTestFactory.Create(SecureGateDatabaseProviderConfigurator.CreateOptions(
			SecureGateDatabaseProvider.SqlServer, connectionString));
		var migrator = context.GetService<IMigrator>();

		await migrator.MigrateAsync();
		(await ObjectCountAsync(connectionString)).Should().Be((1, 3), "política e as 3 funções existem");

		await migrator.MigrateAsync(previous);
		(await ObjectCountAsync(connectionString)).Should().Be((0, 0), "Down remove a política e as funções");

		await migrator.MigrateAsync();
		(await ObjectCountAsync(connectionString)).Should().Be((1, 3));
	}

	private static async Task<(int Policies, int Functions)> ObjectCountAsync(string connectionString)
	{
		await using var connection = await OpenAsync(connectionString, "platform");
		await using var command = connection.CreateCommand();
		command.CommandText =
			"SELECT (SELECT COUNT(*) FROM sys.security_policies WHERE name = N'pol_tenant_isolation'), "
			+ "(SELECT COUNT(*) FROM sys.objects WHERE name IN (N'fn_check_tenant_row', N'fn_check_role_row', N'fn_check_user_row'))";
		await using var reader = await command.ExecuteReaderAsync();
		await reader.ReadAsync();

		return (reader.GetInt32(0), reader.GetInt32(1));
	}

	// ---- Interceptor: troca de escopo com a conexão do contexto aberta ----

	private async Task InScopeAsync(Func<ITenantAccessScope, IDisposable>? enter, Func<ITenantAccessScope, SecureGateDbContext, Task> body)
	{
		using var diScope = factory.Services.CreateScope();
		var accessScope = diScope.ServiceProvider.GetRequiredService<ITenantAccessScope>();
		using var handle = enter?.Invoke(accessScope);

		await body(accessScope, diScope.ServiceProvider.GetRequiredService<SecureGateDbContext>());
	}

	[Fact]
	public async Task Interceptor_TrocaDeEscopoComConexaoAberta_LancaSemDados()
	{
		await InScopeAsync(s => s.EnterTenant(_s.TenantA), async (accessScope, context) =>
		{
			await context.Database.OpenConnectionAsync();

			(await context.Roles.AsNoTracking().CountAsync()).Should().BeGreaterThan(0);

			using var _ = accessScope.EnterPlatform("teste: troca de escopo com conexão aberta");
			var act = () => context.Roles.AsNoTracking().ToListAsync();

			(await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("conexão").And.NotContain(_s.TenantA.ToString());
		});
	}

	[Fact]
	public async Task Interceptor_SemEscopoNaAbertura_ENovoEscopoDepois_Lanca()
	{
		await InScopeAsync(null, async (accessScope, context) =>
		{
			await context.Database.OpenConnectionAsync();
			(await context.Roles.AsNoTracking().CountAsync()).Should().Be(0);

			using var _ = accessScope.EnterTenant(_s.TenantA);
			var act = () => context.Roles.AsNoTracking().ToListAsync();

			await act.Should().ThrowAsync<InvalidOperationException>();
		});
	}

	[Fact]
	public async Task Interceptor_TrocaDeEscopoEmTransacao_Lanca()
	{
		await InScopeAsync(s => s.EnterTenant(_s.TenantA), async (accessScope, context) =>
		{
			await using var transaction = await context.Database.BeginTransactionAsync();

			using var _ = accessScope.EnterTenant(_s.TenantB);
			var act = () => context.Roles.AsNoTracking().ToListAsync();

			await act.Should().ThrowAsync<InvalidOperationException>();
		});
	}

	[Fact]
	public async Task Interceptor_TrocaDeEscopoEntreOperacoes_ReabreERegrava()
	{
		// O caso normal do login: plataforma (acha a pessoa) → tenant (lê os perfis). O EF fecha a conexão por
		// operação, então cada uma abre e grava o contexto do escopo corrente.
		await InScopeAsync(s => s.EnterPlatform("teste: login"), async (accessScope, context) =>
		{
			(await context.Users.AsNoTracking().Select(u => u.Id).ToListAsync()).Should().Contain([_s.Person, _s.OwnerOfB]);

			using var _ = accessScope.EnterTenant(_s.TenantA);
			var roles = await context.Roles.AsNoTracking().ToListAsync();

			roles.Should().NotBeEmpty().And.OnlyContain(r => r.TenantId == _s.TenantA);
		});
	}

	[Fact]
	public async Task Interceptor_GravaContextoSomenteLeitura_NaConexaoDoEf()
	{
		await InScopeAsync(s => s.EnterTenant(_s.TenantA), async (_, context) =>
		{
			await context.Database.OpenConnectionAsync();
			var connection = (SqlConnection)context.Database.GetDbConnection();

			(await SessionValueAsync(connection, "secco_mode")).Should().Be("tenant");
			(await SessionValueAsync(connection, "secco_tenant")).Should().Be(_s.TenantA.ToString("D"));

			var rewrite = () => ExecuteAsync(connection, "EXEC sp_set_session_context @key = N'secco_mode', @value = N'platform';");
			await rewrite.Should().ThrowAsync<SqlException>();
		});
	}

	[Fact]
	public async Task Interceptor_PoolEntreScopesDeDi_NaoVazaEscopo()
	{
		for (var round = 0; round < 3; round++)
		{
			await InScopeAsync(s => s.EnterTenant(_s.TenantA), async (_, context) =>
				(await context.Roles.AsNoTracking().Select(r => r.TenantId).Distinct().ToListAsync()).Should().Equal(_s.TenantA));

			await InScopeAsync(s => s.EnterTenant(_s.TenantB), async (_, context) =>
				(await context.Roles.AsNoTracking().Select(r => r.TenantId).Distinct().ToListAsync()).Should().Equal(_s.TenantB));

			await InScopeAsync(null, async (_, context) =>
				(await context.Roles.AsNoTracking().CountAsync()).Should().Be(0));
		}
	}
}
