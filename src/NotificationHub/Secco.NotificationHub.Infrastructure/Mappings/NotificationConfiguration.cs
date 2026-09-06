using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.NotificationHub.Domain.Notifications;

namespace Secco.NotificationHub.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="Notification"/>. Nomes de tabela/colunas/constraints vêm da
/// <c>SeccoNamingConvention</c> (ADR-0017) — aqui só o que a convention não decide:
/// tamanho das colunas indexadas e índices de consulta.
/// </summary>
internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
	public void Configure(EntityTypeBuilder<Notification> builder)
	{
		ArgumentNullException.ThrowIfNull(builder);

		// Sem teto explícito o EF encolhe a coluna indexada para nvarchar(450) — o máximo que
		// cabe na chave de índice do SQL Server, não uma decisão de modelagem. A constante do
		// domínio é o teto real e vale nos dois engines (ADR-0018).
		builder.Property(notification => notification.Source)
			.HasMaxLength(Notification.SourceMaxLength);

		builder.Property(notification => notification.Type)
			.HasMaxLength(Notification.TypeMaxLength);

		builder.HasIndex(notification => notification.Status);
		builder.HasIndex(notification => notification.CreatedAt);
		builder.HasIndex(notification => new { notification.Source, notification.Type });
	}
}
