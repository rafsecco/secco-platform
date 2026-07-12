using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.LogProcesses;

/// <summary>Busca paginada dos details de um processo; 404 se o processo não existe.</summary>
public sealed class GetLogProcessDetailsHandler(ILogProcessRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="logProcessId">Processo pai.</param>
    /// <param name="page">Paginação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<PagedResult<LogProcessDetailDto>>> HandleAsync(
        Guid logProcessId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!await repository.ExistsAsync(logProcessId, cancellationToken).ConfigureAwait(false))
        {
            return LogStreamErrors.LogProcesses.NotFound;
        }

        var details = await repository.GetDetailsAsync(logProcessId, page, cancellationToken).ConfigureAwait(false);

        return PagedResult.Create(
            details.Items.Select(LogProcessDetailDto.FromEntity).ToList(),
            new PageRequest(details.Page, details.Size),
            details.TotalCount);
    }
}
