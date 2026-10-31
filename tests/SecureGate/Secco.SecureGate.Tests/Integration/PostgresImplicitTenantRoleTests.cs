using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Secco.SecureGate.Infrastructure.Identity;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Paridade do perfil implícito em PostgreSQL (ADR-0018/0056), no mesmo container real da paridade do RLS: a coluna
/// migrou, o índice único filtrado garante um implícito por tenant e o EF persiste a marca com o RLS valendo.
/// </summary>
public sealed class PostgresImplicitTenantRoleTests(PostgresRlsFixture fixture) : IClassFixture<PostgresRlsFixture>
{
	[Fact]
	public async Task AColunaNova_ExisteComOTipoEsperado()
	{
		await using var connection = await fixture.RuntimeAsync("platform");

		await using var command = new NpgsqlCommand(
			"SELECT data_type FROM information_schema.columns WHERE table_name = 'tb_roles' AND column_name = 'fl_implicit'", connection);

		(await command.ExecuteScalarAsync()).Should().Be("boolean");
	}

	[Fact]
	public async Task IndiceUnicoFiltrado_BarraUmSegundoImplicitoNoMesmoTenant_MasNaoOsComuns_NoPostgres()
	{
		await using var connection = await fixture.RuntimeAsync("tenant", fixture.TenantA);

		const string insert = "INSERT INTO tb_roles (id_pk_role, id_fk_tenant, ds_name, ds_normalized_name, fl_implicit) "
			+ "VALUES (@id, @tenant, @name, @normalized, @flag)";

		async Task InsertAsync(string name, bool isImplicit) => await PostgresRlsFixture.ExecuteAsync(
			connection,
			insert,
			("@id", Guid.NewGuid()), ("@tenant", fixture.TenantA), ("@name", name), ("@normalized", name.ToUpperInvariant()), ("@flag", isImplicit));

		await InsertAsync("tenant-member", isImplicit: true);
		await InsertAsync("comum-1", isImplicit: false);
		await InsertAsync("comum-2", isImplicit: false);

		var second = () => InsertAsync("tenant-member-2", isImplicit: true);

		(await second.Should().ThrowAsync<PostgresException>()).Which.SqlState
			.Should().Be("23505", "o índice único filtrado vale também no PostgreSQL");
	}

	[Fact]
	public async Task OutroTenant_PodeTerOSeuImplicito()
	{
		await using var context = PostgresRlsFixture.ContextFor(fixture.RuntimeConnection(), scope => scope.EnterTenant(fixture.TenantC));
		context.Roles.Add(new Role
		{
			Id = Guid.CreateVersion7(),
			TenantId = fixture.TenantC,
			Name = "tenant-member",
			NormalizedName = "TENANT-MEMBER",
			ConcurrencyStamp = Guid.NewGuid().ToString(),
			IsImplicit = true,
		});
		await context.SaveChangesAsync();

		var read = await context.Roles.AsNoTracking().SingleAsync(r => r.TenantId == fixture.TenantC && r.IsImplicit);

		read.Name.Should().Be("tenant-member");
	}
}
