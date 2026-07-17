using Microsoft.EntityFrameworkCore;

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

/// <summary>Aplicação do provider selecionado (assembly de migrations por engine, ADR-0018).</summary>
internal static class NotificationHubDatabaseProviderConfigurator
{
    private const string SqlServerMigrationsAssembly = "Secco.NotificationHub.Migrations.SqlServer";
    private const string PostgresMigrationsAssembly = "Secco.NotificationHub.Migrations.Postgres";

    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        NotificationHubDatabaseProvider provider,
        string connectionString)
    {
        switch (provider)
        {
            case NotificationHubDatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
                break;
            default:
                optionsBuilder.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly));
                break;
        }
    }

    /// <summary>Cria options do contexto para processos fora do request (migrations, manutenção).</summary>
    public static DbContextOptions<Contexts.NotificationHubDbContext> CreateOptions(
        NotificationHubDatabaseProvider provider,
        string connectionString)
    {
        var builder = new DbContextOptionsBuilder<Contexts.NotificationHubDbContext>();
        Configure(builder, provider, connectionString);
        return builder.Options;
    }
}
