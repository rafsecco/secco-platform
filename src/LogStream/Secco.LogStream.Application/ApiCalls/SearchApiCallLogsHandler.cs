using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.ApiCalls;

/// <summary>Busca paginada de chamadas de API do banco do tenant atual.</summary>
public sealed class SearchApiCallLogsHandler(IApiCallLogRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="criteria">Filtros e paginação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<PagedResult<ApiCallLogDto>>> HandleAsync(
        ApiCallLogSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.From is not null && criteria.To is not null && criteria.From > criteria.To)
        {
            return LogStreamErrors.LogEntries.InvalidDateRange;
        }

        var page = await repository.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);

        return PagedResult.Create(
            page.Items.Select(ApiCallLogDto.FromEntity).ToList(),
            new PageRequest(page.Page, page.Size),
            page.TotalCount);
    }
}
