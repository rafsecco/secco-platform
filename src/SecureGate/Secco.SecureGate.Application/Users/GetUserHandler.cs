using Secco.SecureGate.Application.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Detalha um usuário com o acesso efetivo. As permissões saem da mesma resolução que os produtos
/// consultam, perfil a perfil — a tela mostra o que vale, não uma segunda interpretação.
/// </summary>
public sealed class GetUserHandler(IUserDirectory userDirectory, GetRolePermissionsHandler permissions)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<UserDetailDto>> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var account = await userDirectory.GetAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);

		if (account is null)
		{
			return SecureGateErrors.Users.NotFound;
		}

		var effective = new SortedSet<string>(StringComparer.Ordinal);

		foreach (var role in account.Roles)
		{
			var granted = await permissions.HandleAsync(tenantId, role, cancellationToken).ConfigureAwait(false);

			if (granted.IsSuccess)
			{
				effective.UnionWith(granted.Value);
			}
		}

		var status = UserStatuses.From(account.LockoutEnabled, account.LockoutEnd, DateTimeOffset.UtcNow);

		return new UserDetailDto(
			account.Id,
			account.Email,
			account.TenantId,
			status,
			status == UserStatuses.LockedOut ? account.LockoutEnd : null,
			account.Roles,
			[.. effective],
			account.ExternalLogins);
	}
}
