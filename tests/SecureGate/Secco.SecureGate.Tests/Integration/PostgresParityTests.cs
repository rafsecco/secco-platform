using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;
using Npgsql;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SecureGate.Infrastructure.Tenancy;
using Secco.SecureGate.Migrations.Postgres;
using Testcontainers.PostgreSql;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Container PostgreSQL unico para a classe de testes (iniciar um por teste estoura o tempo do initdb em paralelo).
/// </summary>
/// <remarks>
/// Paridade do Row-Level Security em PostgreSQL (ADR-0018/ADR-0041, camada 2). Um container real:
/// as migrations rodam com um papel DONO (nao superusuario) e o runtime e outro papel, sem
/// <c>BYPASSRLS</c>, nao superusuario e nao dono. O filtro do EF nao participa dos testes de SQL cru —
/// o contexto de sessao e gravado como o <c>TenantSessionContextInterceptor</c> o grava
/// (<c>set_config(..., false)</c>), e o que se prova e o BANCO segurando sozinho.
/// </remarks>
public sealed class PostgresRlsFixture : IAsyncLifetime
{
	/// <summary>Imagem fixada, a mesma da paridade do LogStream.</summary>
	private const string PostgresImage = "postgres:15.1";

	internal const string Database = "secco_securegate";
	internal const string OwnerRole = "sg_owner";
	internal const string RuntimeRole = "sg_app";
	internal const string RolePassword = "Sg-Teste-123";

	private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(PostgresImage).Build();

	internal string BaseConnectionString => _container.GetConnectionString();

	internal Guid TenantA { get; private set; }
	internal Guid TenantB { get; private set; }
	internal Guid TenantC { get; private set; }
	internal Guid RoleA { get; private set; }
	internal Guid RoleB { get; private set; }
	internal Guid Person { get; private set; }
	internal Guid OwnerOfB { get; private set; }

	public async Task InitializeAsync()
	{
		await _container.StartAsync();

		// Como superusuario (so aqui): cria os dois papeis e o banco, de propriedade do papel dono
		await using (var admin = new NpgsqlConnection(Connection(_container.GetConnectionString(), "postgres", "postgres")))
		{
			await admin.OpenAsync();
			await ExecuteAsync(admin, $"CREATE ROLE {OwnerRole} LOGIN PASSWORD '{RolePassword}' NOSUPERUSER NOBYPASSRLS");
			await ExecuteAsync(admin, $"CREATE ROLE {RuntimeRole} LOGIN PASSWORD '{RolePassword}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE");
			await ExecuteAsync(admin, $"CREATE DATABASE {Database} OWNER {OwnerRole}");
		}

		// Migrations pelo DONO (ADR-0041: o login que faz DDL nunca e o do servidor)
		await using (var migrator = new SecureGateDbContext(SecureGateDatabaseProviderConfigurator.CreateOptions(
			SecureGateDatabaseProvider.PostgreSql, OwnerConnection())))
		{
			await migrator.Database.MigrateAsync();
		}

		// O runtime so le e escreve: nada de DDL, nada de posse
		await using (var owner = new NpgsqlConnection(OwnerConnection()))
		{
			await owner.OpenAsync();
			await ExecuteAsync(owner, $"GRANT USAGE ON SCHEMA public TO {RuntimeRole}");
			await ExecuteAsync(owner, $"GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {RuntimeRole}");
			await ExecuteAsync(owner, $"GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {RuntimeRole}");
		}

		await SeedAsync();
	}

	public async Task DisposeAsync() => await _container.DisposeAsync();

	// ---- Infra ----

	internal static string Connection(string baseConnectionString, string user, string password, string? database = null) =>
		new NpgsqlConnectionStringBuilder(baseConnectionString)
		{
			Username = user,
			Password = password,
			Database = database ?? new NpgsqlConnectionStringBuilder(baseConnectionString).Database,
			Pooling = false,
		}.ConnectionString;

	internal string OwnerConnection() => Connection(_container.GetConnectionString(), OwnerRole, RolePassword, Database);

	internal string RuntimeConnection() => Connection(_container.GetConnectionString(), RuntimeRole, RolePassword, Database);

	internal string SuperuserConnection() => Connection(_container.GetConnectionString(), "postgres", "postgres", Database);

	internal static async Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
	{
		await using var command = new NpgsqlCommand(sql, connection);

		foreach (var (name, value) in parameters)
		{
			command.Parameters.AddWithValue(name, value);
		}

		return await command.ExecuteNonQueryAsync();
	}

	internal static async Task<long> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
	{
		await using var command = new NpgsqlCommand(sql, connection);

		foreach (var (name, value) in parameters)
		{
			command.Parameters.AddWithValue(name, value);
		}

		return (long)(await command.ExecuteScalarAsync())!;
	}

	/// <summary>Conexao de runtime com o contexto gravado como o interceptor grava (nulo = sem contexto).</summary>
	internal async Task<NpgsqlConnection> RuntimeAsync(string? mode, Guid? tenant = null) =>
		await OpenWithContextAsync(RuntimeConnection(), mode, tenant);

	internal static async Task<NpgsqlConnection> OpenWithContextAsync(string connectionString, string? mode, Guid? tenant = null)
	{
		var connection = new NpgsqlConnection(connectionString);
		await connection.OpenAsync();

		if (mode is not null)
		{
			await using var command = new NpgsqlCommand(
				"select set_config('secco.mode', @mode, false), set_config('secco.tenant', coalesce(@tenant, ''), false)", connection);
			command.Parameters.AddWithValue("@mode", mode);
			command.Parameters.AddWithValue("@tenant", tenant is { } id ? id.ToString("D") : DBNull.Value);
			await command.ExecuteNonQueryAsync();
		}

		return connection;
	}

	internal async Task<long> CountAsync(NpgsqlConnection connection, string table, string where) =>
		// Tabela e filtro vem dos literais deste arquivo — nunca de input
		await ScalarAsync(
			connection,
			$"SELECT COUNT(*) FROM {table} WHERE {where}",
			("@a", TenantA), ("@b", TenantB), ("@roleA", RoleA), ("@roleB", RoleB), ("@person", Person), ("@ownerOfB", OwnerOfB));

	internal static Task<long> TotalAsync(NpgsqlConnection connection, string table) =>
		ScalarAsync(connection, $"SELECT COUNT(*) FROM {table}");

	private async Task SeedAsync()
	{
		// Sem o RLS escrever pelo runtime exigiria contexto: usa o interceptor REAL, em modo plataforma,
		// o que prova tambem o caminho PostgreSQL dele (set_config) contra um Postgres de verdade.
		await using var context = ContextFor(RuntimeConnection(), scope => scope.EnterPlatform("teste: seed da paridade"));

		var tenantA = new Tenant("Tenant A", $"a-{Guid.NewGuid():N}");
		var tenantB = new Tenant("Tenant B", $"b-{Guid.NewGuid():N}");
		var tenantC = new Tenant("Tenant C", $"c-{Guid.NewGuid():N}");
		context.Tenants.AddRange(tenantA, tenantB, tenantC);

		var roleA = NewRole(tenantA.Id, "perfil-a");
		var roleB = NewRole(tenantB.Id, "perfil-b");
		context.Roles.AddRange(roleA, roleB);
		context.RoleClaims.AddRange(
			new RoleClaim { RoleId = roleA.Id, ClaimType = "permission", ClaimValue = "logs:read" },
			new RoleClaim { RoleId = roleB.Id, ClaimType = "permission", ClaimValue = "logs:write" });

		// A pessoa e dona em A e membro em B; o dono de B so existe em B
		var person = NewUser(tenantA.Id, "pessoa");
		var ownerOfB = NewUser(tenantB.Id, "dono-b");
		context.Users.AddRange(person, ownerOfB);
		context.TenantMemberships.AddRange(
			new TenantMembership(person.Id, tenantA.Id, DateTimeOffset.UtcNow),
			new TenantMembership(person.Id, tenantB.Id, DateTimeOffset.UtcNow),
			new TenantMembership(ownerOfB.Id, tenantB.Id, DateTimeOffset.UtcNow));
		await context.SaveChangesAsync();

		TenantA = tenantA.Id;
		TenantB = tenantB.Id;
		TenantC = tenantC.Id;
		RoleA = roleA.Id;
		RoleB = roleB.Id;
		Person = person.Id;
		OwnerOfB = ownerOfB.Id;
	}

	internal static SecureGateDbContext ContextFor(string connectionString, Action<TenantAccessScope> enter)
	{
		var scope = TenantAccessScope.Detached();
		enter(scope);

		var options = new DbContextOptionsBuilder<SecureGateDbContext>(
				SecureGateDatabaseProviderConfigurator.CreateOptions(SecureGateDatabaseProvider.PostgreSql, connectionString))
			.AddInterceptors(new TenantSessionContextInterceptor(
				scope, new SecureGateDatabaseOptions { Provider = SecureGateDatabaseProvider.PostgreSql }))
			.Options;

		return new SecureGateDbContext(options, accessScope: scope);
	}

	internal static Role NewRole(Guid tenantId, string name) => new()
	{
		Id = Guid.CreateVersion7(),
		TenantId = tenantId,
		Name = name,
		NormalizedName = name.ToUpperInvariant(),
		ConcurrencyStamp = Guid.NewGuid().ToString(),
	};

	internal static User NewUser(Guid tenantId, string alias)
	{
		var email = $"{alias}-{Guid.NewGuid():N}@teste.local";

		return new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = tenantId,
			UserName = email,
			NormalizedUserName = email.ToUpperInvariant(),
			Email = email,
			NormalizedEmail = email.ToUpperInvariant(),
			EmailConfirmed = true,
		};
	}
}

/// <summary>
/// Paridade do Row-Level Security em PostgreSQL (ADR-0018/ADR-0041, camada 2): isolamento por SQL cru, escrita
/// bloqueada pelo <c>WITH CHECK</c>, interceptor real, checagem de credencial na subida e o helper de migrations.
/// </summary>
public sealed class PostgresParityTests(PostgresRlsFixture fixture) : IClassFixture<PostgresRlsFixture>
{
	private const string Database = PostgresRlsFixture.Database;
	private const string RolePassword = PostgresRlsFixture.RolePassword;

	private Guid _tenantA => fixture.TenantA;
	private Guid _tenantB => fixture.TenantB;
	private Guid _tenantC => fixture.TenantC;
	private Guid _roleA => fixture.RoleA;
	private Guid _roleB => fixture.RoleB;
	private Guid _person => fixture.Person;
	private Guid _ownerOfB => fixture.OwnerOfB;

	private Task<NpgsqlConnection> RuntimeAsync(string? mode, Guid? tenant = null) => fixture.RuntimeAsync(mode, tenant);

	private static Task<NpgsqlConnection> OpenWithContextAsync(string connectionString, string? mode, Guid? tenant = null) =>
		PostgresRlsFixture.OpenWithContextAsync(connectionString, mode, tenant);

	private Task<long> CountAsync(NpgsqlConnection connection, string table, string where) => fixture.CountAsync(connection, table, where);

	private static Task<long> TotalAsync(NpgsqlConnection connection, string table) => PostgresRlsFixture.TotalAsync(connection, table);

	private static Task<int> ExecuteAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters) =>
		PostgresRlsFixture.ExecuteAsync(connection, sql, parameters);

	private static Task<long> ScalarAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters) =>
		PostgresRlsFixture.ScalarAsync(connection, sql, parameters);

	private string OwnerConnection() => fixture.OwnerConnection();

	private string RuntimeConnection() => fixture.RuntimeConnection();

	private string SuperuserConnection() => fixture.SuperuserConnection();

	private static string Connection(string baseConnectionString, string user, string password, string? database = null) =>
		PostgresRlsFixture.Connection(baseConnectionString, user, password, database);

	private static SecureGateDbContext ContextFor(string connectionString, Action<TenantAccessScope> enter) =>
		PostgresRlsFixture.ContextFor(connectionString, enter);

	private string BaseConnectionString => fixture.BaseConnectionString;

	// ---- O papel de runtime e mesmo sem posse ----

	[Fact]
	public async Task PapelDeRuntime_NaoESuperusuarioNemBypassNemDono()
	{
		await using var connection = await RuntimeAsync("platform");
		await using var command = new NpgsqlCommand(
			"SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user", connection);
		await using var reader = await command.ExecuteReaderAsync();
		await reader.ReadAsync();

		reader.GetBoolean(0).Should().BeFalse();
		reader.GetBoolean(1).Should().BeFalse();
		await reader.CloseAsync();

		var ddl = () => ExecuteAsync(connection, "CREATE TABLE tb_prova_de_posse (id int)");
		(await ddl.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");

		var disable = () => ExecuteAsync(connection, "ALTER TABLE tb_roles DISABLE ROW LEVEL SECURITY");
		(await disable.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", "so o dono desliga o RLS");
	}

	// ---- Isolamento por SQL cru ----

	[Fact]
	public async Task EscopoDeA_VeSoLinhasDeA_EmRolesUsersEMemberships()
	{
		await using var connection = await RuntimeAsync("tenant", _tenantA);

		(await CountAsync(connection, "tb_roles", "id_fk_tenant = @a")).Should().Be(1);
		(await CountAsync(connection, "tb_roles", "id_fk_tenant = @b")).Should().Be(0);
		(await CountAsync(connection, "tb_role_claims", "id_fk_role = @roleA")).Should().Be(1);
		(await CountAsync(connection, "tb_role_claims", "id_fk_role = @roleB")).Should().Be(0);
		(await CountAsync(connection, "tb_tenant_memberships", "id_pfk_tenant = @a")).Should().Be(1);
		(await CountAsync(connection, "tb_tenant_memberships", "id_pfk_tenant = @b")).Should().Be(0);
		(await CountAsync(connection, "tb_users", "id_pk_user = @person")).Should().Be(1);
		(await CountAsync(connection, "tb_users", "id_pk_user = @ownerOfB")).Should().Be(0);
	}

	[Fact]
	public async Task Pessoa_DonaEmAMembroEmB_VistaPelosDois_EDonoDeBSoPorB()
	{
		await using var inB = await RuntimeAsync("tenant", _tenantB);
		(await CountAsync(inB, "tb_users", "id_pk_user = @person")).Should().Be(1, "membro em B");
		(await CountAsync(inB, "tb_users", "id_pk_user = @ownerOfB")).Should().Be(1);

		await using var inC = await RuntimeAsync("tenant", _tenantC);
		(await CountAsync(inC, "tb_users", "id_pk_user = @person OR id_pk_user = @ownerOfB")).Should().Be(0, "nem dono nem membro em C");
	}

	[Fact]
	public async Task SemContexto_NaoMostraNada()
	{
		await using var connection = await RuntimeAsync(mode: null);

		foreach (var table in new[] { "tb_roles", "tb_role_claims", "tb_users", "tb_tenant_memberships" })
		{
			(await TotalAsync(connection, table)).Should().Be(0, $"{table}: sem contexto nada aparece (fail-closed)");
		}
	}

	[Fact]
	public async Task ModoInvalidoETenantMalformado_NaoMostramNada()
	{
		await using (var invalid = await RuntimeAsync("qualquer-coisa", _tenantA))
		{
			(await TotalAsync(invalid, "tb_roles")).Should().Be(0);
		}

		// Tenant sem GUID valido: o cast do predicado falha a consulta (nunca devolve linha); vazio nao casa com ninguem
		await using var tenantWithoutId = await RuntimeAsync("tenant");
		(await TotalAsync(tenantWithoutId, "tb_roles")).Should().Be(0);
		(await TotalAsync(tenantWithoutId, "tb_users")).Should().Be(0);
	}

	[Fact]
	public async Task ModoPlataforma_VeOsDoisTenants()
	{
		await using var connection = await RuntimeAsync("platform");

		(await CountAsync(connection, "tb_roles", "id_fk_tenant = @a")).Should().Be(1);
		(await CountAsync(connection, "tb_roles", "id_fk_tenant = @b")).Should().Be(1);
		(await CountAsync(connection, "tb_users", "id_pk_user = @ownerOfB")).Should().Be(1);
		(await CountAsync(connection, "tb_tenant_memberships", "id_pfk_tenant = @b")).Should().Be(2);
	}

	[Fact]
	public async Task Dono_SemContexto_NaoVeLinhaDeTenant_PorqueOForceVale()
	{
		await using var owner = await OpenWithContextAsync(OwnerConnection(), mode: null);

		(await TotalAsync(owner, "tb_roles")).Should().Be(0, "FORCE ROW LEVEL SECURITY vale tambem para o dono da tabela");
		(await TotalAsync(owner, "tb_users")).Should().Be(0);
	}

	// ---- Escrita bloqueada pelo banco (WITH CHECK) ----

	[Fact]
	public async Task Insert_DeLinhaDeOutroTenant_EBloqueadoPeloBanco()
	{
		await using var connection = await RuntimeAsync("tenant", _tenantA);

		var membership = () => ExecuteAsync(
			connection,
			"INSERT INTO tb_tenant_memberships (id_pfk_user, id_pfk_tenant, fl_active, dt_created_at) VALUES (@u, @t, true, now())",
			("@u", _ownerOfB), ("@t", _tenantC));
		(await membership.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501", "new row violates row-level security policy");

		var claim = () => ExecuteAsync(
			connection,
			"INSERT INTO tb_role_claims (id_fk_role, ds_claim_type, ds_claim_value) VALUES (@roleB, 'permission', 'x:y')",
			("@roleB", _roleB));
		(await claim.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");

		var role = () => ExecuteAsync(
			connection,
			"INSERT INTO tb_roles (id_pk_role, id_fk_tenant, ds_name, ds_normalized_name) VALUES (@id, @b, 'intruso', 'INTRUSO')",
			("@id", Guid.NewGuid()), ("@b", _tenantB));
		(await role.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
	}

	[Fact]
	public async Task Insert_DeLinhaDoProprioTenant_Passa()
	{
		await using var connection = await RuntimeAsync("tenant", _tenantC);
		await using var transaction = await connection.BeginTransactionAsync();

		var inserted = await ExecuteAsync(
			connection,
			"INSERT INTO tb_tenant_memberships (id_pfk_user, id_pfk_tenant, fl_active, dt_created_at) VALUES (@u, @t, true, now())",
			("@u", _ownerOfB), ("@t", _tenantC));

		inserted.Should().Be(1);
		await transaction.RollbackAsync();
	}

	[Fact]
	public async Task Update_QueMoveLinhaParaOutroTenant_EBloqueadoPeloBanco()
	{
		await using var connection = await RuntimeAsync("tenant", _tenantA);

		var act = () => ExecuteAsync(
			connection,
			"UPDATE tb_tenant_memberships SET id_pfk_tenant = @c WHERE id_pfk_user = @u AND id_pfk_tenant = @a",
			("@c", _tenantC), ("@u", _person), ("@a", _tenantA));

		(await act.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
	}

	[Fact]
	public async Task UpdateEDelete_DeLinhaDeOutroTenant_NaoAcertamNada()
	{
		await using var connection = await RuntimeAsync("tenant", _tenantA);

		(await ExecuteAsync(connection, "UPDATE tb_roles SET ds_name = ds_name WHERE id_fk_tenant = @b", ("@b", _tenantB)))
			.Should().Be(0, "a linha de B e invisivel para A: o USING vale para UPDATE");
		(await ExecuteAsync(connection, "DELETE FROM tb_roles WHERE id_fk_tenant = @b", ("@b", _tenantB)))
			.Should().Be(0, "e para DELETE");

		await using var platform = await RuntimeAsync("platform");
		(await CountAsync(platform, "tb_roles", "id_fk_tenant = @b")).Should().Be(1);
	}

	// ---- tb_users: escrita so pelo tenant dono (ADR-0040) ----

	[Fact]
	public async Task Users_EscritaPeloTenantOndeAPessoaESoMembro_NaoAcertaNada()
	{
		await using var connection = await RuntimeAsync("tenant", _tenantB);
		(await CountAsync(connection, "tb_users", "id_pk_user = @person")).Should().Be(1, "membro enxerga");

		(await ExecuteAsync(connection, "UPDATE tb_users SET fl_lockout_enabled = fl_lockout_enabled WHERE id_pk_user = @person", ("@person", _person)))
			.Should().Be(0, "a politica de escrita so concede ao tenant dono: o UPDATE nao enxerga a linha");
		(await ExecuteAsync(connection, "DELETE FROM tb_users WHERE id_pk_user = @person", ("@person", _person)))
			.Should().Be(0);

		await using var platform = await RuntimeAsync("platform");
		(await CountAsync(platform, "tb_users", "id_pk_user = @person")).Should().Be(1, "a linha segue la");
	}

	[Theory]
	[InlineData("tenant")]
	[InlineData("platform")]
	public async Task Users_EscritaPeloTenantDonoEPelaPlataforma_Passa(string mode)
	{
		await using var connection = await RuntimeAsync(mode, mode == "tenant" ? _tenantA : null);
		await using var transaction = await connection.BeginTransactionAsync();

		(await ExecuteAsync(connection, "UPDATE tb_users SET fl_lockout_enabled = fl_lockout_enabled WHERE id_pk_user = @person", ("@person", _person)))
			.Should().Be(1);

		await transaction.RollbackAsync();
	}

	[Fact]
	public async Task Users_MoverODonoOuInserirNoTenantAlheio_EBloqueado()
	{
		await using var connection = await RuntimeAsync("tenant", _tenantA);

		var move = () => ExecuteAsync(connection, "UPDATE tb_users SET id_fk_tenant = @c WHERE id_pk_user = @person", ("@c", _tenantC), ("@person", _person));
		(await move.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");

		var insert = () => ExecuteAsync(
			connection,
			"INSERT INTO tb_users (id_pk_user, id_fk_tenant, fl_email_confirmed, fl_phone_number_confirmed, fl_two_factor_enabled, fl_lockout_enabled, nr_access_failed_count) "
			+ "VALUES (@id, @b, false, false, false, false, 0)",
			("@id", Guid.NewGuid()), ("@b", _tenantB));
		(await insert.Should().ThrowAsync<PostgresException>()).Which.SqlState.Should().Be("42501");
	}

	// ---- Interceptor real contra Postgres real ----

	[Fact]
	public async Task Interceptor_GravaOContextoNaConexaoDoEf_EOBancoOHonra()
	{
		await using var inA = ContextFor(RuntimeConnection(), scope => scope.EnterTenant(_tenantA));
		(await inA.Roles.AsNoTracking().Select(r => r.TenantId).Distinct().ToListAsync()).Should().Equal(_tenantA);

		await using var inB = ContextFor(RuntimeConnection(), scope => scope.EnterTenant(_tenantB));
		(await inB.Roles.AsNoTracking().Select(r => r.TenantId).Distinct().ToListAsync()).Should().Equal(_tenantB);

		await using var none = ContextFor(RuntimeConnection(), _ => { });
		(await none.Roles.AsNoTracking().CountAsync()).Should().Be(0, "escopo ausente nao grava nada: fail-closed");
	}

	// ---- Checagem de credencial na subida (a consulta Postgres da Task 1) ----

	private sealed class CapturingLogger : ILogger
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Entries.Add((logLevel, formatter(state, exception)));
	}

	private static SecureGateDatabaseOptions OptionsFor(string connectionString) =>
		new() { Provider = SecureGateDatabaseProvider.PostgreSql, ConnectionString = connectionString };

	[Fact]
	public async Task Verify_ForaDeDevComSuperusuario_Recusa()
	{
		var act = () => SecureGateRuntimeCredentialCheck.VerifyAsync(
			OptionsFor(SuperuserConnection()), isDevelopment: false, new CapturingLogger());

		var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
		thrown.Message.Should().Contain("superusuário").And.NotContain(RolePassword, "a mensagem nunca carrega a connection string (ADR-0020)");
	}

	[Fact]
	public async Task Verify_ForaDeDevComDonoDasTabelas_Recusa()
	{
		var act = () => SecureGateRuntimeCredentialCheck.VerifyAsync(
			OptionsFor(OwnerConnection()), isDevelopment: false, new CapturingLogger());

		var thrown = (await act.Should().ThrowAsync<InvalidOperationException>()).Which;
		// O unico motivo listado entre parenteses e a posse (nao superusuario nem BYPASSRLS)
		thrown.Message.Should().Contain("privilégio de dono (dono de tabela protegida)");
	}

	[Fact]
	public async Task Verify_EmDevelopmentComDono_SoAvisa()
	{
		var logger = new CapturingLogger();

		await SecureGateRuntimeCredentialCheck.VerifyAsync(OptionsFor(OwnerConnection()), isDevelopment: true, logger);

		logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning)
			.Which.Message.Should().Contain("dono de tabela protegida");
	}

	[Fact]
	public async Task Verify_PapelSemPosse_NaoAvisaNemLanca()
	{
		var logger = new CapturingLogger();

		await SecureGateRuntimeCredentialCheck.VerifyAsync(OptionsFor(RuntimeConnection()), isDevelopment: false, logger);

		logger.Entries.Should().BeEmpty();
	}

	[Fact]
	public async Task Verify_PapelComBypassRls_Recusa()
	{
		await using (var admin = new NpgsqlConnection(SuperuserConnection()))
		{
			await admin.OpenAsync();
			await ExecuteAsync(admin, $"CREATE ROLE sg_bypass LOGIN PASSWORD '{RolePassword}' NOSUPERUSER BYPASSRLS");
			await ExecuteAsync(admin, $"GRANT CONNECT ON DATABASE {Database} TO sg_bypass");
		}

		var act = () => SecureGateRuntimeCredentialCheck.VerifyAsync(
			OptionsFor(Connection(BaseConnectionString, "sg_bypass", RolePassword, Database)),
			isDevelopment: false,
			new CapturingLogger());

		(await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("BYPASSRLS");
	}

	// ---- Helper de migrations ----

	[Fact]
	public async Task DropECreate_DoHelper_SoltamERecriamAsPoliticas()
	{
		string[] tables =
		[
			"tb_roles", "tb_role_claims", "tb_user_roles", "tb_tenant_memberships", "tb_users",
			"tb_tenant_databases", "tb_tenant_federations", "tb_tenant_group_role_mappings", "tb_elevation_grants",
			"tb_tenant_admin_grants",
		];

		await using var owner = new NpgsqlConnection(OwnerConnection());
		await owner.OpenAsync();

		(await StateAsync(owner)).Should().Be((11, 10, 10), "uma politica por tabela (tb_users tem duas: select e write), RLS ligado e forcado nas 10 (revisao da ADR-0044)");

		var drop = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
		TenantRowLevelSecurity.Drop(drop);
		await RunAsync(owner, drop);
		(await StateAsync(owner)).Should().Be((0, 0, 0));

		var create = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
		TenantRowLevelSecurity.Create(create);
		await RunAsync(owner, create);
		(await StateAsync(owner)).Should().Be((11, 10, 10));

		(await ScalarAsync(owner, "SELECT COUNT(*) FROM pg_policies WHERE policyname = ANY (@names)",
			("@names", tables.Where(t => t != "tb_users").Select(t => $"pol_tenant_isolation_{t}")
				.Concat(["pol_tenant_isolation_tb_users_select", "pol_tenant_isolation_tb_users_write"]).ToArray()))).Should().Be(11);
	}

	private static async Task<(long Policies, long Enabled, long Forced)> StateAsync(NpgsqlConnection connection) =>
		(await ScalarAsync(connection, "SELECT COUNT(*) FROM pg_policies WHERE policyname LIKE 'pol_tenant_isolation_%'"),
		 await ScalarAsync(connection, "SELECT COUNT(*) FROM pg_class WHERE relname LIKE 'tb\\_%' AND relrowsecurity"),
		 await ScalarAsync(connection, "SELECT COUNT(*) FROM pg_class WHERE relname LIKE 'tb\\_%' AND relforcerowsecurity"));

	private static async Task RunAsync(NpgsqlConnection connection, MigrationBuilder builder)
	{
		foreach (var operation in builder.Operations.OfType<SqlOperation>())
		{
			await ExecuteAsync(connection, operation.Sql);
		}
	}
}
