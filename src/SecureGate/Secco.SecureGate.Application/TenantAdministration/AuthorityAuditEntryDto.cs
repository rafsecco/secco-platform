namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>Entrada da trilha de autoridade do SecureGate (ADR-0054), como o operador a lê.</summary>
/// <param name="Id">Identificador da entrada (Guid v7, ordenável no tempo).</param>
/// <param name="ActorType">Tipo do ator (<c>client</c> na gestão do tenant por máquina).</param>
/// <param name="ActorId">Identificador do ator — o client id.</param>
/// <param name="ActorName">Nome legível do ator, se houver.</param>
/// <param name="Action">Ação registrada (<c>gestao-do-tenant.&lt;operationId&gt;</c>).</param>
/// <param name="ResourceType">Tipo do recurso afetado.</param>
/// <param name="ResourceId">Identificador do recurso afetado.</param>
/// <param name="Metadata">Metadata em JSON, montada pelo servidor.</param>
/// <param name="OccurredAt">Instante em que o SERVIDOR gravou a entrada.</param>
public sealed record AuthorityAuditEntryDto(
	Guid Id,
	string ActorType,
	string ActorId,
	string? ActorName,
	string Action,
	string ResourceType,
	string ResourceId,
	string? Metadata,
	DateTimeOffset OccurredAt);
