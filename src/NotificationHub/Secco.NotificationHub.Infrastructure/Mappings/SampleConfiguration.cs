using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.NotificationHub.Domain.Samples;

namespace Secco.NotificationHub.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Sample"/>. Nomes de tabela/colunas/constraints vêm da
/// <c>SeccoNamingConvention</c> (ADR-0017) — aqui só o que a convention não decide:
/// índices de consulta.
/// </summary>
internal sealed class SampleConfiguration : IEntityTypeConfiguration<Sample>
{
    public void Configure(EntityTypeBuilder<Sample> builder)
    {
        builder.HasIndex(sample => sample.Name);
        builder.HasIndex(sample => sample.CreatedAt);
    }
}
