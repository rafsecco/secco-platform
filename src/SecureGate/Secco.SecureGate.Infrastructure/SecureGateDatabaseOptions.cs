using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Infrastructure.OpenIddict;

namespace Secco.SecureGate.Infrastructure;

/// <summary>Engines suportados (ADR-0018).</summary>
public enum SecureGateDatabaseProvider
{
    /// <summary>Provider padrão da plataforma.</summary>
    SqlServer = 0,

    /// <summary>Segundo provider suportado.</summary>
    PostgreSql = 1,
}

/// <summary>
/// Banco de PLATAFORMA do SecureGate (seção <c>SecureGate:Database</c>): diferente dos
/// produtos de negócio, aqui a connection string é ÚNICA e fixa (ADR-0022 — identidade
/// não é dado de tenant), não resolvida por catálogo.
/// </summary>
public sealed class SecureGateDatabaseOptions
{
    /// <summary>Engine do banco de plataforma (default: SQL Server, ADR-0018).</summary>
    public SecureGateDatabaseProvider Provider { get; set; } = SecureGateDatabaseProvider.SqlServer;

    /// <summary>Connection string do banco <c>secco_securegate</c>. Obrigatória.</summary>
    public string? ConnectionString { get; set; }
}

/// <summary>
/// Aplicação do provider selecionado (assembly de migrations por engine, ADR-0018).
/// Público: as fábricas de design-time dos assemblies de migrations o utilizam.
/// </summary>
public static class SecureGateDatabaseProviderConfigurator
{
    private const string SqlServerMigrationsAssembly = "Secco.SecureGate.Migrations.SqlServer";
    private const string PostgresMigrationsAssembly = "Secco.SecureGate.Migrations.Postgres";

    public static void Configure(
        DbContextOptionsBuilder optionsBuilder,
        SecureGateDatabaseProvider provider,
        string connectionString)
    {
        switch (provider)
        {
            case SecureGateDatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
                break;
            default:
                optionsBuilder.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly));
                break;
        }

        // Entidades OpenIddict (chave Guid) entram no modelo em TODOS os caminhos de criação
        // do contexto — runtime, migrations e design-time — para o snapshot nunca divergir
        optionsBuilder.UseOpenIddict<OidcApplication, OidcAuthorization, OidcScope, OidcToken, Guid>();
    }

    /// <summary>Cria options do contexto para processos fora do request (migrations, seed).</summary>
    public static DbContextOptions<Contexts.SecureGateDbContext> CreateOptions(
        SecureGateDatabaseProvider provider,
        string connectionString)
    {
        var builder = new DbContextOptionsBuilder<Contexts.SecureGateDbContext>();
        Configure(builder, provider, connectionString);
        return builder.Options;
    }
}
