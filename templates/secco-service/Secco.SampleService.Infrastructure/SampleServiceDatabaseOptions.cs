using Microsoft.EntityFrameworkCore;

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

/// <summary>Aplicação do provider selecionado (assembly de migrations por engine, ADR-0018).</summary>
internal static class SampleServiceDatabaseProviderConfigurator
{
    private const string SqlServerMigrationsAssembly = "Secco.SampleService.Migrations.SqlServer";
    private const string PostgresMigrationsAssembly = "Secco.SampleService.Migrations.Postgres";

    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        SampleServiceDatabaseProvider provider,
        string connectionString)
    {
        switch (provider)
        {
            case SampleServiceDatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
                break;
            default:
                optionsBuilder.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly));
                break;
        }
    }

    /// <summary>Cria options do contexto para processos fora do request (migrations, manutenção).</summary>
    public static DbContextOptions<Contexts.SampleServiceDbContext> CreateOptions(
        SampleServiceDatabaseProvider provider,
        string connectionString)
    {
        var builder = new DbContextOptionsBuilder<Contexts.SampleServiceDbContext>();
        Configure(builder, provider, connectionString);
        return builder.Options;
    }
}
