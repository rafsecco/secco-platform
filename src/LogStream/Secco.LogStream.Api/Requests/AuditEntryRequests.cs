using Secco.LogStream.Domain.Audit;

namespace Secco.LogStream.Api.Requests;

/// <summary>Payload de criação de uma entrada de auditoria.</summary>
/// <param name="ActorId">Identificador do ator: <c>sub</c> do usuário, ou client id. Obrigatório.</param>
/// <param name="ActorType">Natureza do ator.</param>
/// <param name="Action">Ação realizada, verbo canônico (ex.: <c>documento.download</c>). Obrigatória.</param>
/// <param name="ActorName">Snapshot legível do ator no momento do fato, quando informado.</param>
/// <param name="ResourceType">Tipo do recurso afetado, quando informado.</param>
/// <param name="ResourceId">Identificador do recurso afetado, quando informado.</param>
/// <param name="Metadata">JSON livre com contexto adicional, quando informado; validado e limitado em tamanho.</param>
/// <param name="CorrelationId">Correlation id do item; quando presente, vence o header <c>X-Correlation-Id</c>.</param>
/// <param name="OccurredAt">Momento em que o fato aconteceu, declarado pelo chamador; nulo usa o momento corrente.</param>
public sealed record CreateAuditEntryRequest(
	string? ActorId,
	ActorType ActorType,
	string? Action,
	string? ActorName = null,
	string? ResourceType = null,
	string? ResourceId = null,
	string? Metadata = null,
	Guid? CorrelationId = null,
	DateTimeOffset? OccurredAt = null);
