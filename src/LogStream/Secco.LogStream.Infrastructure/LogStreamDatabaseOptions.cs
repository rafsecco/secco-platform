using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.LogStream.Infrastructure;

/// <summary>Engines suportados pelo LogStream (ADR-0018).</summary>
public enum LogStreamDatabaseProvider
{
	/// <summary>Provider padrão da plataforma.</summary>
	SqlServer = 0,

	/// <summary>Segundo provider suportado, com paridade de testes.</summary>
	PostgreSql = 1,
}

/// <summary>
/// Seleção de engine (seção <c>LogStream:Database</c>). Todos os bancos de tenant de um
/// deployment usam o mesmo provider; as connection strings do catálogo devem corresponder.
/// </summary>
public sealed class LogStreamDatabaseOptions
{
	/// <summary>Engine dos bancos de tenant (default: SQL Server, ADR-0018).</summary>
	public LogStreamDatabaseProvider Provider { get; set; } = LogStreamDatabaseProvider.SqlServer;
}

/// <summary>Aplicação do provider selecionado a um options builder (seletor por receita, ADR-0018/ADR-0027).</summary>
internal static class LogStreamDatabaseProviderConfigurator
{
	private static readonly SeccoDatabaseProviderRegistration[] Registrations =
	[
		new(nameof(LogStreamDatabaseProvider.SqlServer),
			(builder, connectionString) => builder.UseSqlServer(connectionString,
				sql => sql.MigrationsAssembly("Secco.LogStream.Migrations.SqlServer"))),
		new(nameof(LogStreamDatabaseProvider.PostgreSql),
			(builder, connectionString) => builder.UseNpgsql(connectionString,
				npgsql => npgsql.MigrationsAssembly("Secco.LogStream.Migrations.Postgres"))),
	];

	public static void Configure(
		DbContextOptionsBuilder optionsBuilder,
		LogStreamDatabaseProvider provider,
		string connectionString) =>
		SeccoDatabaseProviders.Configure(optionsBuilder, provider.ToString(), connectionString, Registrations);

	/// <summary>Cria options do contexto para processos fora do request (migrations, retenção).</summary>
	public static DbContextOptions<Contexts.LogStreamDbContext> CreateOptions(
		LogStreamDatabaseProvider provider,
		string connectionString) =>
		SeccoDatabaseProviders.CreateOptions<Contexts.LogStreamDbContext>(
			provider.ToString(), connectionString, Registrations);
}
