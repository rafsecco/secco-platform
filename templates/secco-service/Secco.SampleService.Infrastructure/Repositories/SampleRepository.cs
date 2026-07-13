using Microsoft.EntityFrameworkCore;
using Secco.SampleService.Application.Samples;
using Secco.SampleService.Domain.Samples;
using Secco.SampleService.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.SampleService.Infrastructure.Repositories;

/// <summary>Persistência de samples no banco do tenant atual.</summary>
internal sealed class SampleRepository(SampleServiceDbContext context) : ISampleRepository
{
    public async Task AddAsync(Sample sample, CancellationToken cancellationToken = default)
    {
        context.Samples.Add(sample);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Sample?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.Samples
            .AsNoTracking()
            .FirstOrDefaultAsync(sample => sample.Id == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<PagedResult<Sample>> SearchAsync(
        SampleSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        var query = context.Samples.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(criteria.NameContains))
        {
            query = query.Where(sample => sample.Name.Contains(criteria.NameContains));
        }

        var page = criteria.EffectivePage;
        var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(sample => sample.CreatedAt)
            .ThenByDescending(sample => sample.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult.Create(items, page, totalCount);
    }
}
