using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Paridade do provisionamento pelo diretório em PostgreSQL (ADR-0018/0055, entrega B), no mesmo container real da
/// paridade do RLS: as colunas novas migraram, o índice único filtrado garante um grupo de provisionamento por tenant
/// e o EF persiste o estado da conta (gerida, origem da desativação) com o RLS valendo.
/// </summary>
public sealed class PostgresDirectoryProvisioningTests(PostgresRlsFixture fixture) : IClassFixture<PostgresRlsFixture>
{
	[Theory]
	[InlineData("tb_users", "fl_directory_managed", "boolean")]
	[InlineData("tb_users", "ie_deactivation_origin", "integer")]
	[InlineData("tb_tenant_group_role_mappings", "fl_provisioning_group", "boolean")]
	[InlineData("tb_tenant_federations", "ds_allowed_email_domains", "character varying")]
	public async Task AsColunasNovas_ExistemComOTipoEsperado(string table, string column, string type)
	{
		await using var connection = await fixture.RuntimeAsync("platform");

		await using var command = new NpgsqlCommand(
			"SELECT data_type FROM information_schema.columns WHERE table_name = @t AND column_name = @c", connection);
		command.Parameters.AddWithValue("@t", table);
		command.Parameters.AddWithValue("@c", column);

		(await command.ExecuteScalarAsync()).Should().Be(type);
	}

	[Fact]
	public async Task IndiceUnicoFiltrado_BarraUmSegundoGrupoDeProvisionamentoNoMesmoTenant_NoPostgres()
	{
		await using var connection = await fixture.RuntimeAsync("tenant", fixture.TenantA);

		const string insert = "INSERT INTO tb_tenant_group_role_mappings "
			+ "(id_pk_tenant_group_role_mapping, id_fk_tenant, entra_group_id, ds_entra_group_display_name, id_fk_role, dt_created_at, fl_provisioning_group) "
			+ "VALUES (@id, @tenant, @group, 'Grupo', @role, now(), @flag)";

		async Task InsertAsync(bool provisioning) => await PostgresRlsFixture.ExecuteAsync(
			connection,
			insert,
			("@id", Guid.NewGuid()), ("@tenant", fixture.TenantA), ("@group", Guid.NewGuid()), ("@role", fixture.RoleA), ("@flag", provisioning));

		await InsertAsync(provisioning: true);
		await InsertAsync(provisioning: false);
		await InsertAsync(provisioning: false);

		var second = () => InsertAsync(provisioning: true);

		(await second.Should().ThrowAsync<PostgresException>()).Which.SqlState
			.Should().Be("23505", "o índice único filtrado vale também no PostgreSQL");
	}

	[Fact]
	public async Task OutroTenant_PodeTerOSeuGrupoDeProvisionamento_EOTenantVizinhoNaoOEnxerga()
	{
		await using (var inB = await fixture.RuntimeAsync("tenant", fixture.TenantB))
		{
			await PostgresRlsFixture.ExecuteAsync(
				inB,
				"INSERT INTO tb_tenant_group_role_mappings "
				+ "(id_pk_tenant_group_role_mapping, id_fk_tenant, entra_group_id, ds_entra_group_display_name, id_fk_role, dt_created_at, fl_provisioning_group) "
				+ "VALUES (@id, @tenant, @group, 'Grupo B', @role, now(), true)",
				("@id", Guid.NewGuid()), ("@tenant", fixture.TenantB), ("@group", Guid.NewGuid()), ("@role", fixture.RoleB));
		}

		await using var inC = await fixture.RuntimeAsync("tenant", fixture.TenantC);
		(await PostgresRlsFixture.TotalAsync(inC, "tb_tenant_group_role_mappings")).Should().Be(0, "o RLS segura a tabela do mapeamento");
	}

	[Fact]
	public async Task ContaGeridaEOrigemDaDesativacao_PersistemPeloEfNoPostgres()
	{
		await using (var context = PostgresRlsFixture.ContextFor(fixture.RuntimeConnection(), scope => scope.EnterTenant(fixture.TenantA)))
		{
			var user = PostgresRlsFixture.NewUser(fixture.TenantA, "gerida");
			user.DirectoryManaged = true;
			user.DeactivationOrigin = DeactivationOrigin.Directory;
			context.Users.Add(user);
			await context.SaveChangesAsync();

			var read = await context.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
			read.DirectoryManaged.Should().BeTrue();
			read.DeactivationOrigin.Should().Be(DeactivationOrigin.Directory);
		}
	}

	[Fact]
	public async Task DominiosDaFederacao_IdaEVoltaNoPostgres()
	{
		await using var context = PostgresRlsFixture.ContextFor(fixture.RuntimeConnection(), scope => scope.EnterTenant(fixture.TenantC));
		var federation = new TenantFederation(fixture.TenantC, Guid.NewGuid());
		federation.SetAllowedEmailDomains(["a.com", "b.com"]);
		context.TenantFederations.Add(federation);
		await context.SaveChangesAsync();

		var read = await context.TenantFederations.AsNoTracking().SingleAsync(f => f.TenantId == fixture.TenantC);

		read.GetAllowedEmailDomains().Should().Equal("a.com", "b.com");
	}
}
