using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.LogStream.Domain.LogEntries;

namespace Secco.LogStream.Infrastructure.Mappings;

/// <summary>
/// Mapeamento da <see cref="LogEntry"/>. Nomes de tabela/colunas/índices vêm da
/// <c>SeccoNamingConvention</c> (ADR-0017) — aqui só o que a convention não decide:
/// índices de consulta. Colunas de texto ficam sem limite no schema: os limites de
/// ingestão são de runtime (configuráveis), não de schema.
/// </summary>
internal sealed class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        // Consultas por período (e por período+severidade) são o acesso dominante
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => new { e.CreatedAt, e.Level });
        builder.HasIndex(e => e.CorrelationId);
    }
}
