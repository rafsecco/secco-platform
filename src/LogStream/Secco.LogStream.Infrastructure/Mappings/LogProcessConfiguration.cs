using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.LogStream.Domain.LogProcesses;

namespace Secco.LogStream.Infrastructure.Mappings;

/// <summary>
/// Mapeamento do agregado <see cref="LogProcess"/>. Nomes vêm da convention (ADR-0017);
/// aqui só relacionamento e índices. Cascade delete: expurgar o processo remove os details
/// (a retenção da fase 4.6 depende disso).
/// </summary>
internal sealed class LogProcessConfiguration : IEntityTypeConfiguration<LogProcess>
{
    public void Configure(EntityTypeBuilder<LogProcess> builder)
    {
        builder.HasMany(process => process.Details)
            .WithOne()
            .HasForeignKey(detail => detail.LogProcessId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(process => process.CreatedAt);
        builder.HasIndex(process => process.Name);
        builder.HasIndex(process => process.CorrelationId);
    }
}

/// <summary>Mapeamento de <see cref="LogProcessDetail"/> — índices de consulta.</summary>
internal sealed class LogProcessDetailConfiguration : IEntityTypeConfiguration<LogProcessDetail>
{
    public void Configure(EntityTypeBuilder<LogProcessDetail> builder)
    {
        builder.HasIndex(detail => detail.CreatedAt);
    }
}
