using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.InstallationOperators;
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
internal sealed class TenantAdminGrantRepository(
	SecureGateDbContext context, ITenantAccessScope accessScope, IInstallationOperatorDesignations designations)
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

	public async Task<bool> IsAdministrativeAccountAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		// Operador designado (ADR-0049): quem administra o tenant dono dele não toca a conta que opera a instalação
		if (designations.IsDesignated(userId))
		{
			return true;
		}

		using (accessScope.EnterPlatform("admins de tenant: alvo protegido é admin de qualquer tenant ou operador"))
		{
			return await context.TenantAdminGrants.AnyAsync(g => g.UserId == userId, cancellationToken).ConfigureAwait(false)
				|| await context.TenantCreatorGrants.AnyAsync(g => g.UserId == userId, cancellationToken).ConfigureAwait(false)
				|| await context.Users
					.AnyAsync(u => u.Id == userId && u.TenantId == SecureGatePlatform.TenantId, cancellationToken)
					.ConfigureAwait(false);
		}
	}

	public async Task<bool> HasAnyAuthorityAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: concessões da pessoa em qualquer tenant ou de criação"))
		{
			return await context.TenantAdminGrants.AnyAsync(g => g.UserId == userId, cancellationToken).ConfigureAwait(false)
				|| await context.TenantCreatorGrants.AnyAsync(g => g.UserId == userId, cancellationToken).ConfigureAwait(false);
		}
	}

	// Dado de plataforma, fora do filtro de tenant: não precisa de modo plataforma
	public Task<bool> HasCreatorGrantAsync(Guid userId, CancellationToken cancellationToken = default) =>
		context.TenantCreatorGrants.AnyAsync(g => g.UserId == userId, cancellationToken);

	public async Task<int> CountCreatedTenantsAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: tenants criados pela pessoa, para o limite"))
		{
			return await context.TenantAdminGrants
				.CountAsync(g => g.UserId == userId && g.CreatedTenant, cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task<bool> OwnsAdministrativeAccountAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: o tenant é dono de conta que administra outros"))
		{
			var owned = context.Users.Where(u => u.TenantId == tenantId).Select(u => u.Id);
			var designated = designations.Current.ToList();

			return (designated.Count > 0
					&& await owned.AnyAsync(id => designated.Contains(id), cancellationToken).ConfigureAwait(false))
				|| await context.TenantAdminGrants.AnyAsync(g => owned.Contains(g.UserId), cancellationToken).ConfigureAwait(false)
				|| await context.TenantCreatorGrants.AnyAsync(g => owned.Contains(g.UserId), cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task AddCreatorGrantIfMissingAsync(TenantCreatorGrant grant, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(grant);

		if (await context.TenantCreatorGrants.AnyAsync(g => g.UserId == grant.UserId, cancellationToken).ConfigureAwait(false))
		{
			return;
		}

		context.TenantCreatorGrants.Add(grant);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	public async Task RemoveCreatorGrantAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var grant = await context.TenantCreatorGrants
			.FirstOrDefaultAsync(g => g.UserId == userId, cancellationToken).ConfigureAwait(false);

		if (grant is not null)
		{
			context.TenantCreatorGrants.Remove(grant);
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task<IReadOnlyList<TenantCreatorDto>> ListCreatorsAsync(CancellationToken cancellationToken = default)
	{
		using (accessScope.EnterPlatform("admins de tenant: quem pode criar tenant é de qualquer tenant"))
		{
			var rows = await context.TenantCreatorGrants.AsNoTracking()
				.Join(context.Users.AsNoTracking(), g => g.UserId, u => u.Id, (g, u) => new { g, u })
				.OrderBy(x => x.u.Email)
				.Select(x => new
				{
					x.u.Id,
					x.u.Email,
					x.u.DisplayName,
					x.u.TenantId,
					x.g.GrantedBy,
					x.g.CreatedAt,
					Created = context.TenantAdminGrants.Count(a => a.UserId == x.u.Id && a.CreatedTenant),
				})
				.ToListAsync(cancellationToken).ConfigureAwait(false);

			return [.. rows.Select(r => new TenantCreatorDto(
				r.Id, r.Email ?? string.Empty, r.DisplayName, r.TenantId, r.GrantedBy, r.CreatedAt, r.Created))];
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
