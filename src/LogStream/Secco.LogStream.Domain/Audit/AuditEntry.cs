using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.LogStream.Domain.Audit;

/// <summary>
/// Trilha de auditoria de uma ação de ator sobre um recurso. Imutável — uma trilha que se
/// pode editar não é trilha. O tenant não é atributo da entidade: o isolamento é físico,
/// por banco (ADR-0005). Entidade própria, não coluna no <see cref="Secco.LogStream.Domain.LogEntries.LogEntry"/>:
/// auditoria vive anos por obrigação legal, log de diagnóstico expira em dias — retenções e
/// campos diferentes, ciclos de vida diferentes (ver design doc).
/// </summary>
public sealed class AuditEntry : BaseEntity
{
	private AuditEntry()
	{
		// Construtor de rehidratação do EF Core
		ActorId = string.Empty;
		Action = string.Empty;
	}

	/// <summary>Cria uma entrada de auditoria.</summary>
	/// <param name="actorId">Identificador do ator: <c>sub</c> do usuário, ou client id. Obrigatório.</param>
	/// <param name="actorType">Natureza do ator.</param>
	/// <param name="action">Ação realizada, verbo canônico (ex.: <c>documento.download</c>). Obrigatória.</param>
	/// <param name="actorName">Snapshot legível do ator no momento do fato, quando informado.</param>
	/// <param name="resourceType">Tipo do recurso afetado, quando informado.</param>
	/// <param name="resourceId">Identificador do recurso afetado, quando informado.</param>
	/// <param name="metadata">JSON livre com contexto adicional, quando informado.</param>
	/// <param name="correlationId">Correlation id da requisição de origem, quando propagado.</param>
	/// <param name="occurredAt">
	/// Momento em que o fato aconteceu, declarado pelo chamador. Nulo usa o momento corrente —
	/// distinto de <see cref="CreatedAt"/>, que é sempre o carimbo do servidor (permite detectar
	/// divergência: relógio errado, reenvio tardio, tentativa de backdating).
	/// </param>
	/// <exception cref="DomainInvariantException">Se o ator ou a ação forem nulos/vazios.</exception>
	public AuditEntry(
		string actorId,
		ActorType actorType,
		string action,
		string? actorName = null,
		string? resourceType = null,
		string? resourceId = null,
		string? metadata = null,
		Guid? correlationId = null,
		DateTimeOffset? occurredAt = null)
	{
		if (string.IsNullOrWhiteSpace(actorId))
		{
			throw new DomainInvariantException("Uma entrada de auditoria exige o identificador do ator.");
		}

		if (string.IsNullOrWhiteSpace(action))
		{
			throw new DomainInvariantException("Uma entrada de auditoria exige a ação realizada.");
		}

		ActorId = actorId;
		ActorType = actorType;
		Action = action;
		ActorName = actorName;
		ResourceType = resourceType;
		ResourceId = resourceId;
		Metadata = metadata;
		CorrelationId = correlationId;
		OccurredAt = occurredAt ?? DateTimeOffset.UtcNow;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Identificador do ator: <c>sub</c> do usuário, ou client id (coluna <c>ds_actor_id</c>).</summary>
	public string ActorId { get; private set; }

	/// <summary>Snapshot legível do ator no momento do fato (coluna <c>ds_actor_name</c>).</summary>
	public string? ActorName { get; private set; }

	/// <summary>Natureza do ator (coluna <c>ie_actor_type</c>).</summary>
	public ActorType ActorType { get; private set; }

	/// <summary>Ação realizada, verbo canônico (coluna <c>ds_action</c>).</summary>
	public string Action { get; private set; }

	/// <summary>Tipo do recurso afetado (coluna <c>ds_resource_type</c>).</summary>
	public string? ResourceType { get; private set; }

	/// <summary>Identificador do recurso afetado (coluna <c>ds_resource_id</c>).</summary>
	public string? ResourceId { get; private set; }

	/// <summary>JSON livre com contexto adicional (coluna <c>ds_metadata</c>).</summary>
	public string? Metadata { get; private set; }

	/// <summary>Correlation id da requisição de origem (coluna <c>correlation_id</c>).</summary>
	public Guid? CorrelationId { get; private set; }

	/// <summary>Momento em que o fato aconteceu, declarado pelo chamador (coluna <c>dt_occurred_at</c>).</summary>
	public DateTimeOffset OccurredAt { get; private set; }

	/// <summary>Momento em que o LogStream registrou o fato (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }
}
