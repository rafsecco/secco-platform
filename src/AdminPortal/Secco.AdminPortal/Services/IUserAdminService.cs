using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>Provisionamento de usuários de um tenant, on-behalf-of o operador (Fase 7.2).</summary>
public interface IUserAdminService
{
	/// <summary>Lista os usuários do tenant com seus roles.</summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<UserSummary>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Cria um usuário no tenant. O operador não define senha (ADR-0033): com
	/// <paramref name="localLogin"/> ligado, o SecureGate envia o convite para a própria pessoa
	/// escolher a credencial.
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="email">E-mail (também o username).</param>
	/// <param name="localLogin">
	/// <see langword="true"/> envia convite por e-mail; <see langword="false"/> cria conta que entra
	/// só pelo diretório corporativo.
	/// </param>
	/// <param name="roles">Roles a atribuir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task CreateUserAsync(
		Guid tenantId, string email, bool localLogin, IReadOnlyList<string> roles,
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

	/// <summary>
	/// Reenvia o convite para a pessoa definir a primeira senha (ADR-0033). Só cabe quando a conta
	/// ainda não tem senha e o login local está ligado — o SecureGate responde 409 fora disso.
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task ResendInviteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Redefine a senha de um usuário pelo admin: manda o link de redefinição e encerra as sessões
	/// abertas na hora (ADR-0033).
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task ResetPasswordAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Liga ou desliga a senha local da conta. Desligar apaga a senha e encerra as sessões abertas;
	/// ligar dispara um convite (ADR-0033).
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="enabled">Situação desejada do login local.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SetLocalLoginAsync(Guid tenantId, Guid userId, bool enabled, CancellationToken cancellationToken = default);

	/// <summary>
	/// Remove o vínculo da conta com um provedor externo. Não bloqueia acesso: enquanto a pessoa
	/// seguir no diretório, o próximo login federado vincula de novo (ADR-0026).
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="provider">Nome do provedor (ex.: <c>EntraId</c>).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoveExternalLoginAsync(Guid tenantId, Guid userId, string provider, CancellationToken cancellationToken = default);

	/// <summary>
	/// Zera o cadastro do segundo fator do usuário. Não isenta: a conta volta a "sem 2FA
	/// cadastrado", e sendo de operador o próximo login cai no cadastro (ADR-0030).
	/// </summary>
	/// <param name="tenantId">Identificador do tenant.</param>
	/// <param name="userId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task ResetTwoFactorAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
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
		Guid tenantId, string email, bool localLogin, IReadOnlyList<string> roles,
		CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.CreateUserAsync(tenantId, new CreateUserRequest
		{
			Email = email,
			LocalLogin = localLogin,
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
			[.. user.ExternalLogins],
			user.HasPassword,
			user.LocalLoginEnabled,
			user.TwoFactorEnabled);
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

	public async Task ResendInviteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.ResendUserInviteAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
	}

	public async Task ResetPasswordAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.ResetUserPasswordAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
	}

	public async Task SetLocalLoginAsync(
		Guid tenantId, Guid userId, bool enabled, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.SetUserLocalLoginAsync(
			tenantId, userId, new SetLocalLoginRequest { Enabled = enabled }, cancellationToken).ConfigureAwait(false);
	}

	public async Task RemoveExternalLoginAsync(
		Guid tenantId, Guid userId, string provider, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.RemoveUserExternalLoginAsync(tenantId, userId, provider, cancellationToken).ConfigureAwait(false);
	}

	public async Task ResetTwoFactorAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.ResetUserTwoFactorAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
	}
}
