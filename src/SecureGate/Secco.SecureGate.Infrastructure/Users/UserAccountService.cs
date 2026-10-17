using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Tenancy;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Infrastructure.Users;

/// <summary>
/// Implementação do <see cref="IUserDirectory"/> sobre o ASP.NET Identity. A atribuição de
/// roles é resolvida DENTRO do tenant (ADR-0021): não usa <c>UserManager.AddToRoleAsync</c>
/// por nome, que consulta o índice global e poderia casar o role de outro tenant.
/// </summary>
internal sealed class UserAccountService(
	UserManager<User> userManager,
	SecureGateDbContext context,
	ITenantAccessScope accessScope) : IUserDirectory
{
	public async Task<Result<UserDto>> CreateAsync(CreateUserData data, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(data);

		// Resolve os roles no tenant do usuário (o handler já validou que existem)
		var normalizedNames = data.Roles.Select(name => name.ToUpperInvariant()).Distinct().ToList();

		var tenantRoles = await context.Roles
			.Where(role => role.TenantId == data.TenantId
				&& role.NormalizedName != null
				&& normalizedNames.Contains(role.NormalizedName))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		if (tenantRoles.Count != normalizedNames.Count)
		{
			return Result.Failure<UserDto>(SecureGateErrors.Users.RoleNotFound);
		}

		var user = new User
		{
			Id = Guid.CreateVersion7(),
			TenantId = data.TenantId,
			UserName = data.Email,
			Email = data.Email,
			LocalLoginEnabled = data.LocalLogin,
			DisplayName = data.DisplayName,
		};

		// Sem senha, sempre (ADR-0033): quem define a credencial é o dono, pelo convite.
		var created = await userManager.CreateAsync(user).ConfigureAwait(false);

		if (!created.Succeeded)
		{
			return Result.Failure<UserDto>(MapIdentityError(created));
		}

		// Vínculo do tenant dono nasce com a conta (ADR-0040): sem ele a pessoa não receberia token
		// nem no tenant que a provisionou.
		context.TenantMemberships.Add(new TenantMembership(user.Id, user.TenantId, DateTimeOffset.UtcNow));

		foreach (var role in tenantRoles)
		{
			context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
		}

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return new UserDto(
			user.Id, user.Email!, user.TenantId, [.. tenantRoles.Select(role => role.Name!)], UserStatuses.Active,
			user.DisplayName, MembershipStates.Owner, user.TenantId);
	}

	public async Task<bool> SetActiveAsync(Guid tenantId, Guid userId, bool active, CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		// Usuário de outro tenant responde como inexistente: a rota é por tenant
		if (user is null || user.TenantId != tenantId)
		{
			return false;
		}

		// A desativação é LOCKOUT, e não uma coluna nova, de propósito: login por senha, login
		// federado, renovação de token e elevação já respeitam lockout — uma única operação passa a
		// valer em todos os caminhos, sem migration e sem um quinto lugar para esquecer de checar.
		if (active)
		{
			// Sem lockout habilitado o usuário já não está bloqueado, e SetLockoutEndDateAsync falharia
			if (await userManager.GetLockoutEnabledAsync(user).ConfigureAwait(false))
			{
				EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, null).ConfigureAwait(false));
			}

			EnsureSucceeded(await userManager.ResetAccessFailedCountAsync(user).ConfigureAwait(false));
		}
		else
		{
			// Ordem obrigatória: IsLockedOutAsync IGNORA a data de bloqueio quando o lockout está
			// desligado, e SetLockoutEndDateAsync falha nesse estado. Ligar primeiro é o que faz a
			// desativação valer de fato.
			EnsureSucceeded(await userManager.SetLockoutEnabledAsync(user, true).ConfigureAwait(false));
			EnsureSucceeded(await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue).ConfigureAwait(false));
		}

		return true;
	}

	private static void EnsureSucceeded(IdentityResult result)
	{
		if (!result.Succeeded)
		{
			// Falha de infraestrutura, não regra de negócio: ativação que não gravou não pode parecer
			// bem-sucedida. Só os códigos, nunca a descrição (ADR-0020).
			throw new InvalidOperationException(
				"O Identity recusou a alteração de estado do usuário: " + string.Join(", ", result.Errors.Select(e => e.Code)));
		}
	}

	public async Task<RoleAssignmentOutcome> AddRoleAsync(
		Guid tenantId,
		Guid userId,
		string roleName,
		RoleAssignmentOrigin origin = RoleAssignmentOrigin.Manual,
		Guid? sourceGroupId = null,
		CancellationToken cancellationToken = default)
	{
		if (await CheckMembershipAsync(tenantId, userId, cancellationToken).ConfigureAwait(false) is { } refusal)
		{
			return refusal;
		}

		var roleId = await FindRoleIdAsync(tenantId, roleName, cancellationToken).ConfigureAwait(false);

		if (roleId is null)
		{
			return RoleAssignmentOutcome.RoleNotFound;
		}

		if (await IsAssignedAsync(userId, roleId.Value, cancellationToken).ConfigureAwait(false))
		{
			return RoleAssignmentOutcome.Done;
		}

		context.UserRoles.Add(new UserRole
		{
			UserId = userId,
			RoleId = roleId.Value,
			Origin = origin,
			SourceGroupId = sourceGroupId,
		});

		try
		{
			await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (DbUpdateException)
		{
			// Duas atribuições simultâneas (clique duplo, retry): a segunda bate na PK. Se a linha existe,
			// o estado pedido foi atingido — idempotente; qualquer outra falha sobe.
			context.ChangeTracker.Clear();

			if (!await IsAssignedAsync(userId, roleId.Value, cancellationToken).ConfigureAwait(false))
			{
				throw;
			}
		}

		return RoleAssignmentOutcome.Done;
	}

	public async Task<RoleAssignmentOutcome> RemoveRoleAsync(
		Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default)
	{
		if (await CheckMembershipAsync(tenantId, userId, cancellationToken).ConfigureAwait(false) is { } refusal)
		{
			return refusal;
		}

		var roleId = await FindRoleIdAsync(tenantId, roleName, cancellationToken).ConfigureAwait(false);

		if (roleId is null)
		{
			return RoleAssignmentOutcome.RoleNotFound;
		}

		var assignment = await context.UserRoles
			.FirstOrDefaultAsync(userRole => userRole.UserId == userId && userRole.RoleId == roleId, cancellationToken)
			.ConfigureAwait(false);

		if (assignment is null)
		{
			return RoleAssignmentOutcome.NotAssigned;
		}

		if (assignment.Origin == RoleAssignmentOrigin.Directory)
		{
			return RoleAssignmentOutcome.BlockedByDirectoryOrigin;
		}

		context.UserRoles.Remove(assignment);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return RoleAssignmentOutcome.Done;
	}

	public async Task<IReadOnlyList<string>> GetRolesInTenantAsync(
		Guid userId, Guid tenantId, CancellationToken cancellationToken = default) =>
		await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where userRole.UserId == userId && role.TenantId == tenantId
			orderby role.Name
			select role.Name!)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public Task<bool> HasRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default)
	{
		var normalized = roleName.ToUpperInvariant();

		return (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			join user in context.Users.AsNoTracking() on userRole.UserId equals user.Id
			where userRole.UserId == userId
				&& user.TenantId == tenantId
				&& role.TenantId == tenantId
				&& role.NormalizedName == normalized
			select userRole.UserId)
			.AnyAsync(cancellationToken);
	}

	public async Task<int> CountActiveOperatorsAsync(Guid excludingUserId, CancellationToken cancellationToken = default)
	{
		// Operadores vivem no tenant de plataforma, que é conhecido (ADR-0041: tenant sempre que houver)
		using var tenantScope = accessScope.EnterTenant(SecureGatePlatform.TenantId);

		var normalized = SecureGatePlatform.OperatorRole.ToUpperInvariant();
		var now = DateTimeOffset.UtcNow;

		return await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			join user in context.Users.AsNoTracking() on userRole.UserId equals user.Id
			where role.TenantId == SecureGatePlatform.TenantId
				&& role.NormalizedName == normalized
				&& user.TenantId == SecureGatePlatform.TenantId
				&& user.Id != excludingUserId
				&& (!user.LockoutEnabled || user.LockoutEnd == null || user.LockoutEnd <= now)
			select user.Id)
			.Distinct()
			.CountAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Atribuir e retirar perfil exigem que a pessoa seja dona ou membro (ativo ou suspenso) do tenant
	/// (ADR-0040): sem vínculo, o perfil do tenant ficaria numa pessoa sem acesso a ele. O dono conta pela
	/// coluna, não pela linha da tabela. <c>null</c> = pode seguir.
	/// </summary>
	private async Task<RoleAssignmentOutcome?> CheckMembershipAsync(
		Guid tenantId, Guid userId, CancellationToken cancellationToken)
	{
		var ownerTenantId = await GetOwnerTenantAsync(userId, cancellationToken).ConfigureAwait(false);

		if (ownerTenantId is null)
		{
			return RoleAssignmentOutcome.UserNotFound;
		}

		if (ownerTenantId == tenantId)
		{
			return null;
		}

		var isMember = await context.TenantMemberships.AsNoTracking()
			.AnyAsync(m => m.TenantId == tenantId && m.UserId == userId, cancellationToken)
			.ConfigureAwait(false);

		return isMember ? null : RoleAssignmentOutcome.NotMember;
	}

	/// <summary>
	/// Perfil por (tenant, nome normalizado) — NUNCA por nome global: o nome só é único por tenant, e a
	/// busca global acharia o perfil homônimo de outro tenant.
	/// </summary>
	private Task<Guid?> FindRoleIdAsync(Guid tenantId, string roleName, CancellationToken cancellationToken)
	{
		var normalized = roleName.ToUpperInvariant();

		return context.Roles
			.AsNoTracking()
			.Where(role => role.TenantId == tenantId && role.NormalizedName == normalized)
			.Select(role => (Guid?)role.Id)
			.FirstOrDefaultAsync(cancellationToken);
	}

	private Task<bool> IsAssignedAsync(Guid userId, Guid roleId, CancellationToken cancellationToken) =>
		context.UserRoles
			.AsNoTracking()
			.AnyAsync(userRole => userRole.UserId == userId && userRole.RoleId == roleId, cancellationToken);

	// "Dono" (User.TenantId), não "membro": o nome ficou ambíguo com a ADR-0040. As operações de pessoa
	// (convite, senha, 2FA, nome, login externo, desativar, sessões, elevação) usam este e só valem pelo dono.
	public Task<bool> BelongsToTenantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
		context.Users
			.AsNoTracking()
			.AnyAsync(user => user.Id == userId && user.TenantId == tenantId, cancellationToken);

	public async Task<IReadOnlyList<UserDto>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		// O dono vem pela coluna; os membros, pelas linhas de vínculo de OUTRO dono (a linha do dono não
		// entra, senão ele apareceria duas vezes)
		var owners = await context.Users
			.AsNoTracking()
			.Where(user => user.TenantId == tenantId)
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		var members = await (
			from membership in context.TenantMemberships.AsNoTracking()
			join user in context.Users.AsNoTracking() on membership.UserId equals user.Id
			where membership.TenantId == tenantId && user.TenantId != tenantId
			select new { User = user, membership.IsActive })
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		var people = owners
			.Select(user => (User: user, Membership: MembershipStates.Owner))
			.Concat(members.Select(member => (
				User: member.User,
				Membership: member.IsActive ? MembershipStates.Member : MembershipStates.Suspended)))
			.OrderBy(person => person.User.UserName, StringComparer.Ordinal)
			.ToList();

		var userIds = people.Select(person => person.User.Id).ToList();

		// Só os perfis DESTE tenant: quem é membro de vários tenants tem perfis em todos eles
		var roleAssignments = await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where userIds.Contains(userRole.UserId) && role.TenantId == tenantId
			select new { userRole.UserId, role.Name })
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		var now = DateTimeOffset.UtcNow;

		return
		[
			.. people.Select(person => new UserDto(
				person.User.Id,
				person.User.Email!,
				tenantId,
				[.. roleAssignments.Where(a => a.UserId == person.User.Id).Select(a => a.Name!)],
				UserStatuses.From(person.User.LockoutEnabled, person.User.LockoutEnd, now),
				person.User.DisplayName,
				person.Membership,
				person.User.TenantId))
		];
	}

	public async Task<UserAccountData?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var user = await context.Users
			.AsNoTracking()
			.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
			.ConfigureAwait(false);

		if (user is null)
		{
			return null;
		}

		var isOwner = user.TenantId == tenantId;
		var membership = MembershipStates.Owner;

		if (!isOwner)
		{
			var link = await context.TenantMemberships
				.AsNoTracking()
				.Where(m => m.TenantId == tenantId && m.UserId == userId)
				.Select(m => (bool?)m.IsActive)
				.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

			if (link is null)
			{
				return null;
			}

			membership = link.Value ? MembershipStates.Member : MembershipStates.Suspended;
		}

		var roleAssignments = await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where userRole.UserId == userId && role.TenantId == tenantId
			orderby role.Name
			select new RoleAssignmentData(role.Name!, userRole.Origin, userRole.SourceGroupId))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		var roles = roleAssignments.Select(assignment => assignment.Name).ToList();

		if (!isOwner)
		{
			// Credencial, federação e segundo fator são da pessoa e do tenant dono: quem administra só o
			// vínculo não precisa deles (ADR-0040)
			return new UserAccountData(
				user.Id, user.Email!, tenantId, user.LockoutEnabled, user.LockoutEnd, roles, [],
				HasPassword: false, LocalLoginEnabled: false, TwoFactorEnabled: false, user.DisplayName,
				roleAssignments, membership, user.TenantId);
		}

		// Só o provedor: a chave (tid:oid do Entra) identifica a pessoa no diretório do cliente
		var logins = await context.UserLogins
			.AsNoTracking()
			.Where(login => login.UserId == userId)
			.Select(login => login.LoginProvider)
			.Distinct()
			.OrderBy(provider => provider)
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		return new UserAccountData(
			user.Id, user.Email!, user.TenantId, user.LockoutEnabled, user.LockoutEnd, roles, logins,
			user.PasswordHash is not null, user.LocalLoginEnabled, user.TwoFactorEnabled, user.DisplayName,
			roleAssignments, membership, user.TenantId);
	}

	public async Task<PersonData?> FindPersonByEmailAsync(string email, CancellationToken cancellationToken = default)
	{
		// A pessoa é de OUTRO tenant dono (ainda não é membro deste): sem tenant que a contenha, só em plataforma
		using var platformScope = accessScope.EnterPlatform("inclusão de membro: a pessoa é achada por e-mail e pertence a outro tenant dono");

		var user = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

		return user is null ? null : new PersonData(user.Id, user.Email!, user.DisplayName, user.TenantId);
	}

	public async Task<Guid?> GetOwnerTenantAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		// Quem vai ser incluído, ter perfil atribuído ou retirado ainda pode NÃO ser membro deste tenant:
		// a regra do vínculo (ADR-0040) precisa do tenant dono dele, que só o modo plataforma enxerga.
		// Só o id do tenant dono sai daqui.
		using var platformScope = accessScope.EnterPlatform("regra de vínculo: tenant dono de uma pessoa que pode não ser membro do tenant da rota");

		return await context.Users
			.AsNoTracking()
			.Where(user => user.Id == userId)
			.Select(user => (Guid?)user.TenantId)
			.FirstOrDefaultAsync(cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<bool> RemoveExternalLoginAsync(
		Guid tenantId,
		Guid userId,
		string provider,
		CancellationToken cancellationToken = default)
	{
		var user = await context.Users
			.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken).ConfigureAwait(false);

		// Usuário de outro tenant responde como inexistente: a rota é por tenant.
		if (user is null || user.TenantId != tenantId)
		{
			return false;
		}

		var links = await context.UserLogins
			.Where(login => login.UserId == userId && login.LoginProvider == provider)
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		if (links.Count == 0)
		{
			return true;
		}

		context.UserLogins.RemoveRange(links);
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return true;
	}

	public async Task<bool> SetDisplayNameAsync(
		Guid tenantId, Guid userId, string? displayName, CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		// Usuário de outro tenant responde como inexistente: a rota é por tenant.
		if (user is null || user.TenantId != tenantId)
		{
			return false;
		}

		user.DisplayName = displayName;

		EnsureSucceeded(await userManager.UpdateAsync(user).ConfigureAwait(false));

		return true;
	}

	public Task<UserSessionState?> GetSessionStateAsync(Guid userId, CancellationToken cancellationToken = default) =>
		context.Users
			.AsNoTracking()
			.Where(user => user.Id == userId)
			.Select(user => new UserSessionState(user.TenantId, user.SecurityStamp, user.LockoutEnabled, user.LockoutEnd))
			.FirstOrDefaultAsync(cancellationToken);

	/// <summary>Traduz o <see cref="IdentityResult"/> em <see cref="Error"/> sem vazar enumeração de e-mail.</summary>
	private static Error MapIdentityError(IdentityResult result)
	{
		if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail"))
		{
			return SecureGateErrors.Users.AlreadyExists;
		}

		var detail = string.Join(" ", result.Errors.Select(error => error.Description));

		return SecureGateErrors.Users.CreationFailed(detail);
	}
}
