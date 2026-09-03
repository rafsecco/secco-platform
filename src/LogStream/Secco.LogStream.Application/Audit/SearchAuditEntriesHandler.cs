using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.Audit;

/// <summary>Busca paginada de entradas de auditoria do banco do tenant atual.</summary>
public sealed class SearchAuditEntriesHandler(IAuditEntryRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="criteria">Filtros e paginação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<AuditEntryDto>>> HandleAsync(
		AuditEntrySearchCriteria criteria,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(criteria);

		if (criteria.From is not null && criteria.To is not null && criteria.From > criteria.To)
		{
			return LogStreamErrors.AuditEntries.InvalidDateRange;
		}

		var page = await repository.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);

		return PagedResult.Create(
			page.Items.Select(AuditEntryDto.FromEntity).ToList(),
			new PageRequest(page.Page, page.Size),
			page.TotalCount);
	}
}
