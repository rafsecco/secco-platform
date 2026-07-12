using Microsoft.EntityFrameworkCore;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.LogStream.Infrastructure.Contexts;

/// <summary>
/// Contexto de dados do LogStream sobre o banco do tenant atual (ADR-0005) —
/// a connection string vem do <c>ITenantConnectionFactory</c> a cada requisição.
/// Herda de <see cref="SeccoDbContext"/>: nomenclatura da ADR-0017 aplicada por convention.
/// </summary>
public sealed class LogStreamDbContext(DbContextOptions<LogStreamDbContext> options)
    : SeccoDbContext(options)
{
    /// <summary>Registros de log gerais (tabela <c>tb_log_entries</c>).</summary>
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    /// <summary>Processos monitorados (tabela <c>tb_log_processes</c>).</summary>
    public DbSet<LogProcess> LogProcesses => Set<LogProcess>();

    /// <summary>Details de processos (tabela <c>tb_log_process_details</c>).</summary>
    public DbSet<LogProcessDetail> LogProcessDetails => Set<LogProcessDetail>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LogStreamDbContext).Assembly);
    }
}
