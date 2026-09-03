using Microsoft.EntityFrameworkCore;
using Secco.LogStream.Application.Audit;
using Secco.LogStream.Domain.Audit;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Infrastructure.Repositories;

/// <summary>Persistência de entradas de auditoria no banco do tenant atual.</summary>
internal sealed class AuditEntryRepository(LogStreamDbContext context) : IAuditEntryRepository
{
	public async Task AddAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default)
	{
		context.AuditEntries.Add(auditEntry);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
		await context.AuditEntries
			.AsNoTracking()
			.FirstOrDefaultAsync(entry => entry.Id == id, cancellationToken)
			.ConfigureAwait(false);

	public async Task<PagedResult<AuditEntry>> SearchAsync(
		AuditEntrySearchCriteria criteria,
		CancellationToken cancellationToken = default)
	{
		var query = context.AuditEntries.AsNoTracking();

		if (criteria.From is not null)
		{
			query = query.Where(entry => entry.OccurredAt >= criteria.From);
		}

		if (criteria.To is not null)
		{
			query = query.Where(entry => entry.OccurredAt <= criteria.To);
		}

		if (criteria.ActorId is not null)
		{
			query = query.Where(entry => entry.ActorId == criteria.ActorId);
		}

		if (criteria.Action is not null)
		{
			query = query.Where(entry => entry.Action == criteria.Action);
		}

		if (criteria.ResourceType is not null)
		{
			query = query.Where(entry => entry.ResourceType == criteria.ResourceType);
		}

		if (criteria.ResourceId is not null)
		{
			query = query.Where(entry => entry.ResourceId == criteria.ResourceId);
		}

		if (criteria.CorrelationId is not null)
		{
			query = query.Where(entry => entry.CorrelationId == criteria.CorrelationId);
		}

		var page = criteria.EffectivePage;
		var totalCount = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

		var items = await query
			.OrderByDescending(entry => entry.OccurredAt)
			.ThenByDescending(entry => entry.Id)
			.Skip(page.Skip)
			.Take(page.Size)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		return PagedResult.Create(items, page, totalCount);
	}
}
