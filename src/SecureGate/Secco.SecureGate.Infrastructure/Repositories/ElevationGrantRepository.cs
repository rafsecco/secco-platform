using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Elevation;
using Secco.SecureGate.Domain.Elevation;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Repositories;

/// <summary>Persistência EF Core da concessão de elevação (ADR-0031) sobre o banco de plataforma.</summary>
internal sealed class ElevationGrantRepository(SecureGateDbContext context) : IElevationGrantRepository
{
	public async Task<ElevationGrant?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
		await context.ElevationGrants
			.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken).ConfigureAwait(false);

	public async Task UpsertAsync(ElevationGrant grant, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(grant);

		// Instância nova (de GetByUserAsync) já está rastreada — só as novas precisam de Add.
		if (context.Entry(grant).State is EntityState.Detached)
		{
			context.ElevationGrants.Add(grant);
		}

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> RemoveAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var grant = await context.ElevationGrants
			.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken).ConfigureAwait(false);

		if (grant is null)
		{
			return false;
		}

		context.ElevationGrants.Remove(grant);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}
}
