using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Tenants;

/// <summary>Implementação do <see cref="ITenantMembershipRepository"/> sobre o banco de plataforma (ADR-0040).</summary>
internal sealed class TenantMembershipRepository(SecureGateDbContext context) : ITenantMembershipRepository
{
	public Task<TenantMembership?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
		context.TenantMemberships
			.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);

	public Task<bool> IsActiveMemberAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
		context.TenantMemberships.AsNoTracking()
			.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId && m.IsActive, cancellationToken);

	public void Add(TenantMembership membership) => context.TenantMemberships.Add(membership);

	public async Task<bool> TryAddAsync(TenantMembership membership, CancellationToken cancellationToken = default)
	{
		context.TenantMemberships.Add(membership);

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateException)
		{
			// Duas inclusões simultâneas (clique duplo, retry): a segunda bate na PK. Se o par existe, o
			// estado pedido já foi atingido; qualquer outra falha sobe.
			context.Entry(membership).State = EntityState.Detached;

			if (await IsMemberRowAsync(membership.TenantId, membership.UserId, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			throw;
		}
	}

	private Task<bool> IsMemberRowAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
		context.TenantMemberships.AsNoTracking()
			.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken);

	public async Task<bool> RemoveAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		// Vínculo e perfis do tenant saem juntos: nunca sobra perfil num tenant sem vínculo (ADR-0040).
		// A estratégia de execução envolve a transação para o caso de retry configurado no provider.
		var strategy = context.Database.CreateExecutionStrategy();

		return await strategy.ExecuteAsync(async () =>
		{
			await using var transaction = await context.Database
				.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

			var tenantRoleIds = context.Roles.Where(role => role.TenantId == tenantId).Select(role => role.Id);

			await context.UserRoles
				.Where(assignment => assignment.UserId == userId && tenantRoleIds.Contains(assignment.RoleId))
				.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

			var removed = await context.TenantMemberships
				.Where(m => m.TenantId == tenantId && m.UserId == userId)
				.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

			await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

			return removed > 0;
		}).ConfigureAwait(false);
	}

	public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		context.SaveChangesAsync(cancellationToken);
}
