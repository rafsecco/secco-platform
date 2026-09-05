using Secco.LogStream.Domain.Audit;

namespace Secco.LogStream.Application.Audit;

/// <summary>Representação de leitura de uma entrada de auditoria — a entidade nunca cruza a borda HTTP.</summary>
/// <param name="Id">Identificador da entrada.</param>
/// <param name="ActorId">Identificador do ator: <c>sub</c> do usuário, ou client id.</param>
/// <param name="ActorName">Snapshot legível do ator no momento do fato, quando informado.</param>
/// <param name="ActorType">Natureza do ator.</param>
/// <param name="Action">Ação realizada, verbo canônico.</param>
/// <param name="ResourceType">Tipo do recurso afetado, quando informado.</param>
/// <param name="ResourceId">Identificador do recurso afetado, quando informado.</param>
/// <param name="Metadata">JSON livre com contexto adicional, quando informado.</param>
/// <param name="CorrelationId">Correlation id de origem, quando propagado.</param>
/// <param name="OccurredAt">Momento em que o fato aconteceu, declarado pelo chamador.</param>
/// <param name="CreatedAt">Momento em que o LogStream registrou o fato.</param>
public sealed record AuditEntryDto(
	Guid Id,
	string ActorId,
	string? ActorName,
	ActorType ActorType,
	string Action,
	string? ResourceType,
	string? ResourceId,
	string? Metadata,
	Guid? CorrelationId,
	DateTimeOffset OccurredAt,
	DateTimeOffset CreatedAt)
{
	/// <summary>Projeta a entidade para o DTO.</summary>
	public static AuditEntryDto FromEntity(AuditEntry entity) =>
		new(entity.Id, entity.ActorId, entity.ActorName, entity.ActorType, entity.Action,
			entity.ResourceType, entity.ResourceId, entity.Metadata, entity.CorrelationId,
			entity.OccurredAt, entity.CreatedAt);
}
