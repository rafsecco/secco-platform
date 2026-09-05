using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Secco.LogStream.Domain.Audit;

namespace Secco.LogStream.Infrastructure.Mappings;

/// <summary>
/// Mapeamento de <see cref="AuditEntry"/>. Nomes de tabela/coluna vêm da convention
/// (ADR-0017); aqui os três eixos de consulta que a trilha de auditoria precisa responder:
/// mais recentes primeiro, histórico de um ator e histórico de um recurso.
/// </summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
	public void Configure(EntityTypeBuilder<AuditEntry> builder)
	{
		builder.HasIndex(entry => entry.OccurredAt)
			.IsDescending();

		builder.HasIndex(entry => new { entry.ActorId, entry.OccurredAt })
			.IsDescending(false, true);

		builder.HasIndex(entry => new { entry.ResourceType, entry.ResourceId });
	}
}
