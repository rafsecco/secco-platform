using Microsoft.EntityFrameworkCore;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Infrastructure.Repositories;

/// <summary>Persistência de registros de log no banco do tenant atual.</summary>
internal sealed class LogEntryRepository(LogStreamDbContext context) : ILogEntryRepository
{
    public async Task AddAsync(LogEntry logEntry, CancellationToken cancellationToken = default)
    {
        context.LogEntries.Add(logEntry);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<LogEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.LogEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<PagedResult<LogEntry>> SearchAsync(
        LogEntrySearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = context.LogEntries.AsNoTracking();

        if (criteria.From is not null)
        {
            query = query.Where(entry => entry.CreatedAt >= criteria.From);
        }

        if (criteria.To is not null)
        {
            query = query.Where(entry => entry.CreatedAt <= criteria.To);
        }

        if (criteria.Level is not null)
        {
            query = query.Where(entry => entry.Level == criteria.Level);
        }

        if (!string.IsNullOrWhiteSpace(criteria.MessageContains))
        {
            // Substring (LIKE); full-text por provider chega na fase 4.7
            query = query.Where(entry => entry.Message.Contains(criteria.MessageContains));
        }

        if (criteria.CorrelationId is not null)
        {
            query = query.Where(entry => entry.CorrelationId == criteria.CorrelationId);
        }

        var page = criteria.EffectivePage;
        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult.Create(items, page, totalCount);
    }
}
