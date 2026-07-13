using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.SampleService.Application.Samples;

/// <summary>Busca paginada de samples do banco do tenant atual.</summary>
public sealed class SearchSamplesHandler(ISampleRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="criteria">Filtros e paginação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<PagedResult<SampleDto>>> HandleAsync(
        SampleSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var page = await repository.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);

        return PagedResult.Create(
            page.Items.Select(SampleDto.FromEntity).ToList(),
            new PageRequest(page.Page, page.Size),
            page.TotalCount);
    }
}
