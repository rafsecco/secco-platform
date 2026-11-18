using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Secco.SecureGate.Application.Users;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Paridade dos atributos de pessoa do diretório em PostgreSQL (ADR-0018/0055), no mesmo container real da
/// paridade do RLS: as colunas migraram com tipo e tamanho e o EF persiste os atributos com o RLS valendo.
/// </summary>
public sealed class PostgresDirectoryAttributesTests(PostgresRlsFixture fixture) : IClassFixture<PostgresRlsFixture>
{
	[Theory]
	[InlineData("ds_job_title", "character varying", 128)]
	[InlineData("ds_department", "character varying", 128)]
	[InlineData("ds_business_phone", "character varying", 64)]
	[InlineData("id_fk_manager", "uuid", null)]
	[InlineData("ie_attributes_origin", "integer", null)]
	public async Task AsColunasNovas_ExistemComTipoETamanhoEsperados(string column, string type, int? length)
	{
		await using var connection = await fixture.RuntimeAsync("platform");

		await using var command = new NpgsqlCommand(
			"SELECT data_type, character_maximum_length FROM information_schema.columns "
			+ "WHERE table_name = 'tb_users' AND column_name = @c", connection);
		command.Parameters.AddWithValue("@c", column);

		await using var reader = await command.ExecuteReaderAsync();
		(await reader.ReadAsync()).Should().BeTrue($"a coluna {column} deve existir");
		reader.GetString(0).Should().Be(type);
		(reader.IsDBNull(1) ? null : (int?)reader.GetInt32(1)).Should().Be(length);
	}

	[Fact]
	public async Task AtributosEGestor_IdaEVoltaPeloEfComRlsEVizinhoNaoEnxerga()
	{
		var manager = PostgresRlsFixture.NewUser(fixture.TenantA, "gestor");
		var person = PostgresRlsFixture.NewUser(fixture.TenantA, "pessoa");

		await using (var context = PostgresRlsFixture.ContextFor(fixture.RuntimeConnection(), scope => scope.EnterTenant(fixture.TenantA)))
		{
			context.Users.Add(manager);
			await context.SaveChangesAsync();

			person.JobTitle = "Analista";
			person.Department = "TI";
			person.BusinessPhone = "+55 11 5555-0000";
			person.ManagerId = manager.Id;
			person.AttributesOrigin = DirectoryAttributesOrigin.Directory;
			context.Users.Add(person);
			await context.SaveChangesAsync();

			var read = await context.Users.AsNoTracking().SingleAsync(u => u.Id == person.Id);
			read.Should().BeEquivalentTo(new
			{
				JobTitle = "Analista",
				Department = "TI",
				BusinessPhone = "+55 11 5555-0000",
				ManagerId = (Guid?)manager.Id,
				AttributesOrigin = DirectoryAttributesOrigin.Directory,
			});
		}

		await using var neighbour = PostgresRlsFixture.ContextFor(fixture.RuntimeConnection(), scope => scope.EnterTenant(fixture.TenantB));
		(await neighbour.Users.AsNoTracking().AnyAsync(u => u.Id == person.Id)).Should().BeFalse("o RLS esconde a conta do vizinho");
	}
}
