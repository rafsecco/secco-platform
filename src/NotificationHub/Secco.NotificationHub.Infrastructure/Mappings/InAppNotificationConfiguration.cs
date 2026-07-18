using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.NotificationHub.Domain.InAppNotifications;

namespace Secco.NotificationHub.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="InAppNotification"/>. Nomes de tabela/colunas/constraints vêm
/// da <c>SeccoNamingConvention</c> (ADR-0017) — aqui só o que a convention não decide:
/// o índice composto que sustenta a listagem/contagem de não lidas por usuário.
/// </summary>
internal sealed class InAppNotificationConfiguration : IEntityTypeConfiguration<InAppNotification>
{
    public void Configure(EntityTypeBuilder<InAppNotification> builder)
    {
        builder.HasIndex(notification => new { notification.UserId, notification.IsRead });
    }
}
