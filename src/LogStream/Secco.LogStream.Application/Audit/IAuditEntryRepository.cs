using Secco.LogStream.Domain.Audit;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Application.Audit;

/// <summary>Filtros da busca de entradas de auditoria. Todos opcionais; combinados com AND; sempre igualdade exata (nunca <c>LIKE</c>).</summary>
/// <param name="From">Ocorridas a partir deste momento (inclusive).</param>
/// <param name="To">Ocorridas até este momento (inclusive).</param>
/// <param name="ActorId">Identificador do ator exato.</param>
/// <param name="Action">Ação exata.</param>
/// <param name="ResourceType">Tipo de recurso exato.</param>
/// <param name="ResourceId">Identificador de recurso exato.</param>
/// <param name="CorrelationId">Correlation id exato.</param>
/// <param name="Page">Paginação (1-based, normalizada pelo <see cref="PageRequest"/>).</param>
public sealed record AuditEntrySearchCriteria(
	DateTimeOffset? From = null,
	DateTimeOffset? To = null,
	string? ActorId = null,
	string? Action = null,
	string? ResourceType = null,
	string? ResourceId = null,
	Guid? CorrelationId = null,
	PageRequest? Page = null)
{
	/// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
	public PageRequest EffectivePage => Page ?? PageRequest.Default;
}

/// <summary>Porta de persistência/consulta de entradas de auditoria — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IAuditEntryRepository
{
	/// <summary>Persiste uma entrada de auditoria.</summary>
	/// <param name="auditEntry">Entrada a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default);

	/// <summary>Busca uma entrada pelo identificador.</summary>
	/// <param name="id">Identificador da entrada.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Busca paginada com os filtros informados, ordenada da mais recente à mais antiga.</summary>
	/// <param name="criteria">Filtros e paginação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PagedResult<AuditEntry>> SearchAsync(AuditEntrySearchCriteria criteria, CancellationToken cancellationToken = default);
}
