using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>Provisionamento de usuários de um tenant, on-behalf-of o operador (Fase 7.2).</summary>
public interface IUserAdminService
{
	/// <summary>Lista os usuários do tenant com seus roles.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<UserSummary>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Cria um usuário no tenant (senha hasheada no servidor pelo SecureGate).</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="email">E-mail (também o username).</param>
	/// <param name="password">Senha inicial.</param>
	/// <param name="roles">Roles a atribuir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task CreateUserAsync(
		Guid tenantId, string email, string password, IReadOnlyList<string> roles,
		CancellationToken cancellationToken = default);

	/// <summary>Obtém o detalhe de um usuário, com permissões efetivas e logins externos.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<UserDetail> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Atribui um perfil a um usuário existente.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="role">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken = default);

	/// <summary>Remove um perfil de um usuário.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="role">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoveRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken = default);

	/// <summary>Ativa ou desativa um usuário.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="active">Situação desejada: <see langword="true"/> ativa, <see langword="false"/> desativa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SetActiveAsync(Guid tenantId, Guid userId, bool active, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class SecureGateUserAdminService(ISecureGateClientFactory clientFactory) : IUserAdminService
{
	public async Task<IReadOnlyList<UserSummary>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var users = await client.ListUsersAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return [.. users.Select(user => new UserSummary(user.Id, user.Email, [.. user.Roles], user.Status))];
	}

	public async Task CreateUserAsync(
		Guid tenantId, string email, string password, IReadOnlyList<string> roles,
		CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.CreateUserAsync(tenantId, new CreateUserRequest
		{
			Email = email,
			Password = password,
			Roles = [.. roles],
		}, cancellationToken).ConfigureAwait(false);
	}

	public async Task<UserDetail> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var user = await client.GetUserAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);

		return new UserDetail(
			user.Id,
			user.Email,
			user.Status,
			user.LockoutEnd,
			[.. user.Roles],
			[.. user.EffectivePermissions],
			[.. user.ExternalLogins]);
	}

	public async Task AddRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.AddUserRoleAsync(tenantId, userId, role, cancellationToken).ConfigureAwait(false);
	}

	public async Task RemoveRoleAsync(Guid tenantId, Guid userId, string role, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.RemoveUserRoleAsync(tenantId, userId, role, cancellationToken).ConfigureAwait(false);
	}

	public async Task SetActiveAsync(Guid tenantId, Guid userId, bool active, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		if (active)
		{
			await client.ActivateUserAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			await client.DeactivateUserAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
		}
	}
}
