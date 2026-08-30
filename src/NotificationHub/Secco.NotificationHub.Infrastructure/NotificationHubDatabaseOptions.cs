using Microsoft.EntityFrameworkCore;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.NotificationHub.Infrastructure;

/// <summary>Engines suportados (ADR-0018).</summary>
public enum NotificationHubDatabaseProvider
{
	/// <summary>Provider padrão da plataforma.</summary>
	SqlServer = 0,

	/// <summary>Segundo provider suportado.</summary>
	PostgreSql = 1,
}

/// <summary>
/// Seleção de engine (seção <c>NotificationHub:Database</c>). Todos os bancos de tenant de um
/// deployment usam o mesmo provider; as connection strings do catálogo devem corresponder.
/// </summary>
public sealed class NotificationHubDatabaseOptions
{
	/// <summary>Engine dos bancos de tenant (default: SQL Server, ADR-0018).</summary>
	public NotificationHubDatabaseProvider Provider { get; set; } = NotificationHubDatabaseProvider.SqlServer;
}

/// <summary>Aplicação do provider selecionado (seletor por receita, ADR-0018/ADR-0027).</summary>
internal static class NotificationHubDatabaseProviderConfigurator
{
	private static readonly SeccoDatabaseProviderRegistration[] Registrations =
	[
		new(nameof(NotificationHubDatabaseProvider.SqlServer),
			(builder, connectionString) => builder.UseSqlServer(connectionString,
				sql => sql.MigrationsAssembly("Secco.NotificationHub.Migrations.SqlServer"))),
		new(nameof(NotificationHubDatabaseProvider.PostgreSql),
			(builder, connectionString) => builder.UseNpgsql(connectionString,
				npgsql => npgsql.MigrationsAssembly("Secco.NotificationHub.Migrations.Postgres"))),
	];

	public static void Configure(
		DbContextOptionsBuilder optionsBuilder,
		NotificationHubDatabaseProvider provider,
		string connectionString) =>
		SeccoDatabaseProviders.Configure(optionsBuilder, provider.ToString(), connectionString, Registrations);

	/// <summary>Cria options do contexto para processos fora do request (migrations, manutenção).</summary>
	public static DbContextOptions<Contexts.NotificationHubDbContext> CreateOptions(
		NotificationHubDatabaseProvider provider,
		string connectionString) =>
		SeccoDatabaseProviders.CreateOptions<Contexts.NotificationHubDbContext>(
			provider.ToString(), connectionString, Registrations);
}
