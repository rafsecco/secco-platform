using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.TenantAdministration;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Repositories;

/// <summary>
/// Persistência EF Core das concessões de admin de tenant (ADR-0044). As leituras que olham a pessoa
/// ou atravessam tenants entram no modo plataforma aqui dentro, cada uma com o próprio motivo
/// (ADR-0041): a pessoa pode ser dona de outro tenant e nem ser membro do administrado.
/// </summary>
internal sealed class TenantAdminGrantRepository(SecureGateDbContext context, ITenantAccessScope accessScope)
	: ITenantAdminGrantRepository
{
	public Task<bool> ExistsAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
		context.TenantAdminGrants.AnyAsync(g => g.TenantId == tenantId && g.UserId == userId, cancellationToken);

	public async Task AddAsync(TenantAdminGrant grant, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(grant);

		context.TenantAdminGrants.Add(grant);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task<bool> RemoveAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var grant = await context.TenantAdminGrants
			.FirstOrDefaultAsync(g => g.TenantId == tenantId && g.UserId == userId, cancellationToken)
			.ConfigureAwait(false);

		if (grant is null)
		{
			return false;
		}

		context.TenantAdminGrants.Remove(grant);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}

	public async Task<IReadOnlyList<TenantAdminDto>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: a pessoa concedida pode ser dona de outro tenant"))
		{
			return await context.TenantAdminGrants.AsNoTracking()
				.Where(g => g.TenantId == tenantId)
				.Join(context.Users.AsNoTracking(), g => g.UserId, u => u.Id, (g, u) => new { g, u })
				.OrderBy(x => x.u.Email)
				.Select(x => new TenantAdminDto(
					x.u.Id, x.u.Email ?? string.Empty, x.u.DisplayName, x.u.TenantId, x.g.GrantedBy, x.g.CreatedAt))
				.ToListAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: a pessoa concedida pode ser dona de outro tenant"))
		{
			return await context.Users.AnyAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task<bool> HasAnyAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: concessões da pessoa em qualquer tenant"))
		{
			return await context.TenantAdminGrants.AnyAsync(g => g.UserId == userId, cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task<bool> HasGrantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: concessões da pessoa em qualquer tenant"))
		{
			return await context.TenantAdminGrants
				.AnyAsync(g => g.TenantId == tenantId && g.UserId == userId, cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task<IReadOnlyList<TenantDto>> ListTenantsForUserAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: concessões da pessoa em qualquer tenant"))
		{
			var tenants = await context.TenantAdminGrants.AsNoTracking()
				.Where(g => g.UserId == userId)
				.Join(context.Tenants.AsNoTracking(), g => g.TenantId, t => t.Id, (g, t) => t)
				.OrderBy(t => t.Name)
				.ToListAsync(cancellationToken).ConfigureAwait(false);

			return [.. tenants.Select(TenantDto.FromEntity)];
		}
	}
}
