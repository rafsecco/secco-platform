using Microsoft.EntityFrameworkCore;
using Secco.LogStream.Application.ApiCalls;
using Secco.LogStream.Domain.ApiCalls;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Infrastructure.Repositories;

/// <summary>Persistência/consulta de chamadas de API no banco do tenant atual.</summary>
internal sealed class ApiCallLogRepository(LogStreamDbContext context) : IApiCallLogRepository
{
    public async Task AddAsync(ApiCallLog apiCallLog, CancellationToken cancellationToken = default)
    {
        context.ApiCallLogs.Add(apiCallLog);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ApiCallLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.ApiCallLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(call => call.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<PagedResult<ApiCallLog>> SearchAsync(
        ApiCallLogSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = context.ApiCallLogs.AsNoTracking();

        if (criteria.From is not null)
        {
            query = query.Where(call => call.CreatedAt >= criteria.From);
        }

        if (criteria.To is not null)
        {
            query = query.Where(call => call.CreatedAt <= criteria.To);
        }

        if (criteria.IsSuccess is not null)
        {
            query = query.Where(call => call.IsSuccess == criteria.IsSuccess);
        }

        if (!string.IsNullOrWhiteSpace(criteria.HttpMethod))
        {
            var method = criteria.HttpMethod.ToUpperInvariant();
            query = query.Where(call => call.HttpMethod == method);
        }

        if (!string.IsNullOrWhiteSpace(criteria.UrlContains))
        {
            query = query.Where(call => call.Url.Contains(criteria.UrlContains));
        }

        if (criteria.ResponseStatusCode is not null)
        {
            query = query.Where(call => call.ResponseStatusCode == criteria.ResponseStatusCode);
        }

        if (criteria.CorrelationId is not null)
        {
            query = query.Where(call => call.CorrelationId == criteria.CorrelationId);
        }

        var page = criteria.EffectivePage;
        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(call => call.CreatedAt)
            .ThenByDescending(call => call.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult.Create(items, page, totalCount);
    }
}
