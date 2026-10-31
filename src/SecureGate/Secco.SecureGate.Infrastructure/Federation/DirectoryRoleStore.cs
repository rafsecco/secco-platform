using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Infrastructure.Federation;

/// <summary>
/// Persistência da reconciliação de perfis (ADR-0055) sobre o ASP.NET Identity e o banco de plataforma. Roda no
/// escopo do tenant que o chamador entrou (ADR-0041): o filtro do EF e o RLS isolam; os <c>Where</c> explícitos
/// por tenant só tornam a intenção legível e impedem um id de outro tenant de passar.
/// </summary>
internal sealed class DirectoryRoleStore(SecureGateDbContext context) : IDirectoryRoleStore
{
	public async Task<IReadOnlyList<DirectoryLinkedAccount>> ListLinkedAccountsAsync(
		Guid tenantId, Guid directoryId, CancellationToken cancellationToken = default)
	{
		var prefix = DirectoryLinkKey.Prefix(directoryId);

		// Só contas DONAS do tenant: a federação é do tenant dono da conta (ADR-0026/0040)
		var links = await (
			from login in context.UserLogins.AsNoTracking()
			join user in context.Users.AsNoTracking() on login.UserId equals user.Id
			where login.LoginProvider == DirectoryLinkKey.Provider
				&& login.ProviderKey.StartsWith(prefix)
				&& user.TenantId == tenantId
			select new
			{
				user.Id,
				login.ProviderKey,
				user.DirectoryManaged,
				user.DeactivationOrigin,
				user.LockoutEnabled,
				user.LockoutEnd,
			})
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		var accounts = new Dictionary<Guid, DirectoryLinkedAccount>();
		var now = DateTimeOffset.UtcNow;

		foreach (var link in links)
		{
			// Chave malformada ou de outro formato: ignorada, nunca adivinhada
			if (DirectoryLinkKey.TryGetObjectId(link.ProviderKey, directoryId, out var objectId))
			{
				accounts.TryAdd(
					link.Id,
					new DirectoryLinkedAccount(
						link.Id,
						objectId,
						link.DirectoryManaged,
						link.DeactivationOrigin,
						UserStatuses.From(link.LockoutEnabled, link.LockoutEnd, now) == UserStatuses.Deactivated));
			}
		}

		return [.. accounts.Values];
	}

	public async Task<IReadOnlyList<DirectoryAssignment>> ListAssignmentsAsync(
		Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(userIds);

		var ids = userIds.ToList();

		return await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where role.TenantId == tenantId && ids.Contains(userRole.UserId)
			select new DirectoryAssignment(userRole.UserId, role.Id, role.Name!, userRole.Origin, userRole.SourceGroupId))
			.ToListAsync(cancellationToken).ConfigureAwait(false);
	}

	public Task<int> CountDirectoryAssignmentsAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		(
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where role.TenantId == tenantId && userRole.Origin == RoleAssignmentOrigin.Directory
			select userRole.UserId)
			.CountAsync(cancellationToken);

	public async Task<bool> AddDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, Guid sourceGroupId, CancellationToken cancellationToken = default)
	{
		// Perfil e conta precisam ser do tenant: um id de outro tenant não vira atribuição por engano
		var roleInTenant = await context.Roles.AsNoTracking()
			.AnyAsync(role => role.Id == roleId && role.TenantId == tenantId, cancellationToken).ConfigureAwait(false);
		var userInTenant = await context.Users.AsNoTracking()
			.AnyAsync(user => user.Id == userId && user.TenantId == tenantId, cancellationToken).ConfigureAwait(false);

		if (!roleInTenant || !userInTenant)
		{
			return false;
		}

		if (await context.UserRoles.AsNoTracking()
			.AnyAsync(userRole => userRole.UserId == userId && userRole.RoleId == roleId, cancellationToken).ConfigureAwait(false))
		{
			return false;
		}

		context.UserRoles.Add(new UserRole
		{
			UserId = userId,
			RoleId = roleId,
			Origin = RoleAssignmentOrigin.Directory,
			SourceGroupId = sourceGroupId,
		});

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return true;
		}
		catch (DbUpdateException)
		{
			// Corrida com outra atribuição (login e ciclo ao mesmo tempo): a segunda bate na PK. Se a linha
			// existe o estado pedido foi atingido; qualquer outra falha sobe.
			context.ChangeTracker.Clear();

			if (await context.UserRoles.AsNoTracking()
				.AnyAsync(userRole => userRole.UserId == userId && userRole.RoleId == roleId, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			throw;
		}
	}

	public async Task<bool> RemoveDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default)
	{
		var assignment = await FindDirectoryAssignmentAsync(tenantId, userId, roleId, cancellationToken).ConfigureAwait(false);

		if (assignment is null)
		{
			return false;
		}

		context.UserRoles.Remove(assignment);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}

	public async Task<bool> RetargetDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, Guid sourceGroupId, CancellationToken cancellationToken = default)
	{
		var assignment = await FindDirectoryAssignmentAsync(tenantId, userId, roleId, cancellationToken).ConfigureAwait(false);

		if (assignment is null)
		{
			return false;
		}

		assignment.SourceGroupId = sourceGroupId;
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}

	public async Task<bool> IsLastActiveMemberAsync(
		Guid tenantId, Guid roleId, Guid userId, CancellationToken cancellationToken = default)
	{
		var now = DateTimeOffset.UtcNow;

		// Membros do perfil que estão ativos: conta não bloqueada nem desativada e, se não for dona do tenant,
		// com vínculo ativo nele
		var activeMembers = await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			join user in context.Users.AsNoTracking() on userRole.UserId equals user.Id
			where role.Id == roleId
				&& role.TenantId == tenantId
				&& (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
				&& (user.TenantId == tenantId
					|| context.TenantMemberships.Any(m => m.UserId == user.Id && m.TenantId == tenantId && m.IsActive))
			select user.Id)
			.Distinct()
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		// Conta que já não é membro ativo: tirá-la não muda o conjunto dos ativos
		return activeMembers.Contains(userId) && activeMembers.Count == 1;
	}

	private Task<UserRole?> FindDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken) =>
		(
			from userRole in context.UserRoles
			join role in context.Roles on userRole.RoleId equals role.Id
			where userRole.UserId == userId
				&& userRole.RoleId == roleId
				&& role.TenantId == tenantId
				&& userRole.Origin == RoleAssignmentOrigin.Directory
			select userRole)
			.FirstOrDefaultAsync(cancellationToken);
}
