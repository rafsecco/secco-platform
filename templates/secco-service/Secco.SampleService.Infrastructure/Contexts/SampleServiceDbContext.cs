using Microsoft.EntityFrameworkCore;
using Secco.SampleService.Domain.Samples;
using Secco.SDK.EntityFrameworkCore;

namespace Secco.SampleService.Infrastructure.Contexts;

/// <summary>
/// Contexto de dados sobre o banco do tenant atual (ADR-0005) — a connection string vem
/// do <c>ITenantConnectionFactory</c> a cada requisição. Herda de <see cref="SeccoDbContext"/>:
/// nomenclatura da ADR-0017 aplicada por convention — ninguém digita nomes de coluna.
/// </summary>
public sealed class SampleServiceDbContext(DbContextOptions<SampleServiceDbContext> options)
    : SeccoDbContext(options)
{
    /// <summary>Samples (tabela <c>tb_samples</c>).</summary>
    public DbSet<Sample> Samples => Set<Sample>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SampleServiceDbContext).Assembly);
    }
}
