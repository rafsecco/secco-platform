using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.SampleService.Infrastructure;

/// <summary>Engines suportados (ADR-0018).</summary>
public enum SampleServiceDatabaseProvider
{
	/// <summary>Provider padrão da plataforma.</summary>
	SqlServer = 0,

	/// <summary>Segundo provider suportado.</summary>
	PostgreSql = 1,
}

/// <summary>
/// Seleção de engine (seção <c>SampleService:Database</c>). Todos os bancos de tenant de um
/// deployment usam o mesmo provider; as connection strings do catálogo devem corresponder.
/// </summary>
public sealed class SampleServiceDatabaseOptions
{
	/// <summary>Engine dos bancos de tenant (default: SQL Server, ADR-0018).</summary>
	public SampleServiceDatabaseProvider Provider { get; set; } = SampleServiceDatabaseProvider.SqlServer;
}

/// <summary>Aplicação do provider selecionado (seletor por receita, ADR-0018/ADR-0027).</summary>
internal static class SampleServiceDatabaseProviderConfigurator
{
	private static readonly SeccoDatabaseProviderRegistration[] Registrations =
	[
		new(nameof(SampleServiceDatabaseProvider.SqlServer),
			(builder, connectionString) => builder.UseSqlServer(connectionString,
				sql => sql.MigrationsAssembly("Secco.SampleService.Migrations.SqlServer"))),
		new(nameof(SampleServiceDatabaseProvider.PostgreSql),
			(builder, connectionString) => builder.UseNpgsql(connectionString,
				npgsql => npgsql.MigrationsAssembly("Secco.SampleService.Migrations.Postgres"))),
	];

	public static void Configure(
		DbContextOptionsBuilder optionsBuilder,
		SampleServiceDatabaseProvider provider,
		string connectionString) =>
		SeccoDatabaseProviders.Configure(optionsBuilder, provider.ToString(), connectionString, Registrations);

	/// <summary>Cria options do contexto para processos fora do request (migrations, manutenção).</summary>
	public static DbContextOptions<Contexts.SampleServiceDbContext> CreateOptions(
		SampleServiceDatabaseProvider provider,
		string connectionString) =>
		SeccoDatabaseProviders.CreateOptions<Contexts.SampleServiceDbContext>(
			provider.ToString(), connectionString, Registrations);
}
