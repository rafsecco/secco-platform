using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.NotificationHub.Domain.Notifications;

namespace Secco.NotificationHub.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Notification"/>. Nomes de tabela/colunas/constraints vêm da
/// <c>SeccoNamingConvention</c> (ADR-0017) — aqui só o que a convention não decide:
/// índices de consulta.
/// </summary>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.HasIndex(notification => notification.Status);
        builder.HasIndex(notification => notification.CreatedAt);
    }
}
