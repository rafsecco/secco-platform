using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SharedKernel.Pagination;

namespace Secco.SecureGate.Infrastructure.TenantAdministration;

/// <summary>
/// Persistência EF Core da trilha de autoridade guardada no SecureGate (ADR-0054). Escrita e leitura rodam no
/// escopo do tenant (filtro <c>"Tenant"</c> + RLS, ADR-0041); só a retenção atravessa tenants, em modo plataforma.
/// </summary>
internal sealed class AuthorityAuditRepository(SecureGateDbContext context, ITenantAccessScope accessScope)
	: IAuthorityAuditRepository
{
	public async Task AddAsync(AuthorityAuditEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);

		context.AuthorityAuditEntries.Add(entry);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<PagedResult<AuthorityAuditEntryDto>> ListByTenantAsync(
		Guid tenantId, PageRequest page, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(page);

		// O filtro do escopo já restringe ao tenant da rota; o Where explícito mantém a intenção legível
		var query = context.AuthorityAuditEntries.AsNoTracking().Where(e => e.TenantId == tenantId);
		var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

		var items = await query
			.OrderByDescending(e => e.CreatedAt)
			.ThenByDescending(e => e.Id)
			.Skip(page.Skip)
			.Take(page.Size)
			.Select(e => new AuthorityAuditEntryDto(
				e.Id, e.ActorType, e.ActorId, e.ActorName, e.Action, e.ResourceType, e.ResourceId, e.Metadata, e.CreatedAt))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		return PagedResult.Create<AuthorityAuditEntryDto>(items, page, total);
	}

	public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, int batchSize, CancellationToken cancellationToken = default)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

		using (accessScope.EnterPlatform("retenção da trilha de autoridade: apaga o vencido de todos os tenants, fora de requisição"))
		{
			var ids = await context.AuthorityAuditEntries.AsNoTracking()
				.Where(e => e.CreatedAt < cutoff)
				.OrderBy(e => e.CreatedAt)
				.Select(e => e.Id)
				.Take(batchSize)
				.ToListAsync(cancellationToken).ConfigureAwait(false);

			if (ids.Count == 0)
			{
				return 0;
			}

			return await context.AuthorityAuditEntries
				.Where(e => ids.Contains(e.Id) && e.CreatedAt < cutoff)
				.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		}
	}
}
