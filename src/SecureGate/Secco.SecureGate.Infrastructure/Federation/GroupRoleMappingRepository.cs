using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure.Federation;

/// <summary>Implementação do <see cref="IGroupRoleMappingRepository"/> sobre o banco de plataforma (ADR-0036).</summary>
internal sealed class GroupRoleMappingRepository(SecureGateDbContext context) : IGroupRoleMappingRepository
{
	public Task<TenantGroupRoleMapping?> FindByGroupAsync(
		Guid tenantId, Guid entraGroupId, CancellationToken cancellationToken = default) =>
		context.TenantGroupRoleMappings
			.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.EntraGroupId == entraGroupId, cancellationToken);

	public Task<TenantGroupRoleMapping?> GetByIdAsync(
		Guid tenantId, Guid mappingId, CancellationToken cancellationToken = default) =>
		context.TenantGroupRoleMappings
			.FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == mappingId, cancellationToken);

	public async Task<IReadOnlyList<GroupRoleMappingRecord>> ListAsync(
		Guid tenantId, CancellationToken cancellationToken = default) =>
		await (
			from mapping in context.TenantGroupRoleMappings.AsNoTracking()
			join role in context.Roles.AsNoTracking() on mapping.RoleId equals role.Id
			where mapping.TenantId == tenantId
			orderby mapping.EntraGroupDisplayName
			select new GroupRoleMappingRecord(
				mapping.Id, mapping.EntraGroupId, mapping.EntraGroupDisplayName, mapping.RoleId, role.Name!, mapping.CreatedAt))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

	public Task AddAsync(TenantGroupRoleMapping mapping, CancellationToken cancellationToken = default)
	{
		context.TenantGroupRoleMappings.Add(mapping);

		return Task.CompletedTask;
	}

	public Task RemoveAsync(TenantGroupRoleMapping mapping, CancellationToken cancellationToken = default)
	{
		context.TenantGroupRoleMappings.Remove(mapping);

		return Task.CompletedTask;
	}

	public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		context.SaveChangesAsync(cancellationToken);
}
