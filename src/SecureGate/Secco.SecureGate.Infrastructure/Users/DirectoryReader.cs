using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Directory;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Pagination;

namespace Secco.SecureGate.Infrastructure.Users;

/// <summary>
/// Diretório de pessoas de um tenant (ADR-0045): donos da conta e membros com vínculo ativo. Roda no
/// escopo de tenant (ADR-0041) e ainda filtra o tenant explicitamente; vínculo suspenso não entra.
/// </summary>
internal sealed class DirectoryReader(SecureGateDbContext context) : IDirectoryReader
{
	private IQueryable<User> People(Guid tenantId) =>
		context.Users.AsNoTracking().Where(user =>
			user.TenantId == tenantId
			|| context.TenantMemberships.Any(m => m.UserId == user.Id && m.TenantId == tenantId && m.IsActive));

	public async Task<PagedResult<DirectoryPersonData>> ListAsync(
		Guid tenantId, PageRequest page, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(page);

		var people = People(tenantId);
		var total = await people.LongCountAsync(cancellationToken).ConfigureAwait(false);

		var items = await people
			.OrderBy(user => user.Email)
			.ThenBy(user => user.Id)
			.Skip(page.Skip)
			.Take(page.Size)
			.Select(user => new DirectoryPersonData(
				user.Id, user.Email!, user.DisplayName, user.LockoutEnabled, user.LockoutEnd, user.TenantId == tenantId))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		return PagedResult.Create<DirectoryPersonData>(items, page, total);
	}

	public Task<DirectoryPersonData?> FindAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
		People(tenantId)
			.Where(user => user.Id == userId)
			.Select(user => new DirectoryPersonData(
				user.Id, user.Email!, user.DisplayName, user.LockoutEnabled, user.LockoutEnd, user.TenantId == tenantId))
			.FirstOrDefaultAsync(cancellationToken);
}
