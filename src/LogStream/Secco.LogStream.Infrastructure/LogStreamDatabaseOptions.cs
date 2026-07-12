using Microsoft.EntityFrameworkCore;

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

/// <summary>Aplicação do provider selecionado a um options builder (assembly de migrations por engine, ADR-0018).</summary>
internal static class LogStreamDatabaseProviderConfigurator
{
    private const string SqlServerMigrationsAssembly = "Secco.LogStream.Migrations.SqlServer";
    private const string PostgresMigrationsAssembly = "Secco.LogStream.Migrations.Postgres";

    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        LogStreamDatabaseProvider provider,
        string connectionString)
    {
        switch (provider)
        {
            case LogStreamDatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
                break;
            default:
                optionsBuilder.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly));
                break;
        }
    }

    /// <summary>Cria options do contexto para processos fora do request (migrations, retenção).</summary>
    public static DbContextOptions<Contexts.LogStreamDbContext> CreateOptions(
        LogStreamDatabaseProvider provider,
        string connectionString)
    {
        var builder = new DbContextOptionsBuilder<Contexts.LogStreamDbContext>();
        Configure(builder, provider, connectionString);
        return builder.Options;
    }
}
