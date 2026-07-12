using Microsoft.EntityFrameworkCore;
using Secco.LogStream.Application.LogProcesses;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Infrastructure.Repositories;

/// <summary>
/// Persistência/consulta de processos no banco do tenant atual. O status agregado é
/// computado no SQL (MAX(ie_level) por subquery) — details nunca são carregados em
/// memória para listagem, ao contrário do legado.
/// </summary>
internal sealed class LogProcessRepository(LogStreamDbContext context) : ILogProcessRepository
{
    public async Task AddAsync(LogProcess logProcess, CancellationToken cancellationToken = default)
    {
        context.LogProcesses.Add(logProcess);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddDetailAsync(LogProcessDetail detail, CancellationToken cancellationToken = default)
    {
        context.LogProcessDetails.Add(detail);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.LogProcesses
            .AsNoTracking()
            .AnyAsync(process => process.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<LogProcessSummary?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await ProjectToSummary(context.LogProcesses.AsNoTracking().Where(process => process.Id == id))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<PagedResult<LogProcessSummary>> SearchAsync(
        LogProcessSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = context.LogProcesses.AsNoTracking();

        if (criteria.From is not null)
        {
            query = query.Where(process => process.CreatedAt >= criteria.From);
        }

        if (criteria.To is not null)
        {
            query = query.Where(process => process.CreatedAt <= criteria.To);
        }

        if (!string.IsNullOrWhiteSpace(criteria.NameContains))
        {
            query = query.Where(process => process.Name.Contains(criteria.NameContains));
        }

        if (criteria.CorrelationId is not null)
        {
            query = query.Where(process => process.CorrelationId == criteria.CorrelationId);
        }

        if (criteria.Status is not null)
        {
            query = FilterByStatus(query, criteria.Status.Value);
        }

        var page = criteria.EffectivePage;
        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var items = await ProjectToSummary(
                query.OrderByDescending(process => process.CreatedAt).ThenByDescending(process => process.Id))
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult.Create(items, page, totalCount);
    }

    public async Task<PagedResult<LogProcessDetail>> GetDetailsAsync(
        Guid logProcessId,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        var query = context.LogProcessDetails
            .AsNoTracking()
            .Where(detail => detail.LogProcessId == logProcessId);

        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(detail => detail.CreatedAt)
            .ThenByDescending(detail => detail.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult.Create(items, page, totalCount);
    }

    private static IQueryable<LogProcessSummary> ProjectToSummary(IQueryable<LogProcess> query) =>
        query.Select(process => new LogProcessSummary(
            process.Id,
            process.Name,
            process.ExternalReference,
            process.CorrelationId,
            process.CreatedAt,
            process.Details.Count,
            process.Details.Max(detail => (LogEntryLevel?)detail.Level)));

    /// <summary>
    /// Traduz o status agregado para a agregação SQL — o espelho em query da
    /// <see cref="ProcessStatusRule"/> do domínio (validado por teste de integração).
    /// </summary>
    private static IQueryable<LogProcess> FilterByStatus(IQueryable<LogProcess> query, ProcessStatus status) =>
        status switch
        {
            ProcessStatus.Critical => query.Where(p =>
                p.Details.Max(d => (LogEntryLevel?)d.Level) >= LogEntryLevel.Critical),
            ProcessStatus.Error => query.Where(p =>
                p.Details.Max(d => (LogEntryLevel?)d.Level) == LogEntryLevel.Error),
            ProcessStatus.Warning => query.Where(p =>
                p.Details.Max(d => (LogEntryLevel?)d.Level) == LogEntryLevel.Warning),
            _ => query.Where(p =>
                p.Details.Max(d => (LogEntryLevel?)d.Level) == null
                || p.Details.Max(d => (LogEntryLevel?)d.Level) <= LogEntryLevel.Information),
        };
}
