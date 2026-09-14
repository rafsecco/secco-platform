using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Infrastructure.Users;

/// <summary>
/// Implementação do <see cref="IUserDirectory"/> sobre o ASP.NET Identity. A atribuição de
/// roles é resolvida DENTRO do tenant (ADR-0021): não usa <c>UserManager.AddToRoleAsync</c>
/// por nome, que consulta o índice global e poderia casar o role de outro tenant.
/// </summary>
internal sealed class UserAccountService(UserManager<User> userManager, SecureGateDbContext context) : IUserDirectory
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
		};

		var created = await userManager.CreateAsync(user, data.Password).ConfigureAwait(false);

		if (!created.Succeeded)
		{
			return Result.Failure<UserDto>(MapIdentityError(created));
		}

		foreach (var role in tenantRoles)
		{
			context.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
		}

		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return new UserDto(user.Id, user.Email!, user.TenantId, [.. tenantRoles.Select(role => role.Name!)]);
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

	public Task<bool> BelongsToTenantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
		context.Users
			.AsNoTracking()
			.AnyAsync(user => user.Id == userId && user.TenantId == tenantId, cancellationToken);

	public async Task<IReadOnlyList<UserDto>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var users = await context.Users
			.AsNoTracking()
			.Where(user => user.TenantId == tenantId)
			.OrderBy(user => user.UserName)
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		var userIds = users.Select(user => user.Id).ToList();

		var roleAssignments = await (
			from userRole in context.UserRoles.AsNoTracking()
			join role in context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
			where userIds.Contains(userRole.UserId)
			select new { userRole.UserId, role.Name })
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		return
		[
			.. users.Select(user => new UserDto(
				user.Id,
				user.Email!,
				user.TenantId,
				[.. roleAssignments.Where(a => a.UserId == user.Id).Select(a => a.Name!)]))
		];
	}

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
