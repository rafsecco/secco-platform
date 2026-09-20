using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Infrastructure.Credentials;

/// <summary>
/// Links de credencial sobre os provedores de token do ASP.NET Identity (ADR-0033).
/// </summary>
/// <remarks>
/// Os nomes dos provedores chegam por construtor e não por constante daqui: quem os registra é a
/// composição da Api, junto das validades.
/// <para>
/// <b>Uso único sem tabela:</b> o token do Identity embute o <c>SecurityStamp</c>. Definir a senha
/// troca o stamp, e todo link pendente daquela conta deixa de validar no mesmo instante.
/// </para>
/// </remarks>
/// <param name="userManager">Gerenciador de usuários do Identity.</param>
/// <param name="context">Contexto do banco de plataforma.</param>
/// <param name="providers">Nomes dos provedores de token (convite e redefinição).</param>
internal sealed class IdentityCredentialTokens(
	UserManager<User> userManager,
	SecureGateDbContext context,
	CredentialTokenProviderNames providers) : ICredentialTokens
{
	/// <inheritdoc />
	public async Task<CredentialAccount?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
		await ToAccountAsync(await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false), cancellationToken)
			.ConfigureAwait(false);

	/// <inheritdoc />
	public async Task<CredentialAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default) =>
		await ToAccountAsync(await userManager.FindByEmailAsync(email).ConfigureAwait(false), cancellationToken)
			.ConfigureAwait(false);

	/// <inheritdoc />
	public Task<string> CreateInviteTokenAsync(Guid userId, CancellationToken cancellationToken = default) =>
		CreateTokenAsync(userId, providers.Invite, providers.InvitePurpose);

	/// <inheritdoc />
	public Task<string> CreateResetTokenAsync(Guid userId, CancellationToken cancellationToken = default) =>
		CreateTokenAsync(userId, providers.Reset, providers.ResetPurpose);

	/// <inheritdoc />
	public async Task<CredentialTokenOutcome> SetPasswordAsync(
		Guid userId,
		string token,
		bool invite,
		string newPassword,
		CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		if (user is null)
		{
			return CredentialTokenOutcome.InvalidToken;
		}

		if (!await CanReceiveCredentialMailAsync(user, cancellationToken).ConfigureAwait(false))
		{
			// Mesmo desfecho de token inválido para quem olha de fora: conta desativada, tenant
			// desligado ou login local desabilitado não podem ser distinguidos pelo link.
			return CredentialTokenOutcome.NotAllowed;
		}

		var provider = invite ? providers.Invite : providers.Reset;
		var purpose = invite ? providers.InvitePurpose : providers.ResetPurpose;

		if (!await userManager.VerifyUserTokenAsync(user, provider, purpose, token).ConfigureAwait(false))
		{
			return CredentialTokenOutcome.InvalidToken;
		}

		// Convite é AddPassword (não há hash ainda); redefinição remove o antigo e põe o novo.
		if (await userManager.HasPasswordAsync(user).ConfigureAwait(false))
		{
			var removed = await userManager.RemovePasswordAsync(user).ConfigureAwait(false);

			if (!removed.Succeeded)
			{
				return CredentialTokenOutcome.NotAllowed;
			}
		}

		var result = await userManager.AddPasswordAsync(user, newPassword).ConfigureAwait(false);

		if (!result.Succeeded)
		{
			return CredentialTokenOutcome.WeakPassword;
		}

		// Troca o stamp explicitamente: é o que mata os links pendentes e o que a ADR-0032 usa
		// como versão de sessão. O AddPassword sozinho não garante a troca em todos os caminhos.
		await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);

		return CredentialTokenOutcome.Done;
	}

	/// <inheritdoc />
	public async Task<CredentialTokenOutcome> ChangeOwnPasswordAsync(
		Guid userId,
		string currentPassword,
		string newPassword,
		CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		if (user is null || !await CanReceiveCredentialMailAsync(user, cancellationToken).ConfigureAwait(false))
		{
			return CredentialTokenOutcome.NotAllowed;
		}

		if (!await userManager.CheckPasswordAsync(user, currentPassword).ConfigureAwait(false))
		{
			// Exigir a senha atual é o que impede uma sessão sequestrada de trocar a credencial
			// e expulsar o dono da própria conta (ADR-0033).
			return CredentialTokenOutcome.InvalidToken;
		}

		var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword).ConfigureAwait(false);

		return result.Succeeded ? CredentialTokenOutcome.Done : CredentialTokenOutcome.WeakPassword;
	}

	/// <inheritdoc />
	public async Task RemovePasswordAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		if (await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false) is { } user
			&& await userManager.HasPasswordAsync(user).ConfigureAwait(false))
		{
			await userManager.RemovePasswordAsync(user).ConfigureAwait(false);
			await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);
		}
	}

	/// <inheritdoc />
	public async Task SetLocalLoginAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default)
	{
		var user = await context.Users.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken)
			.ConfigureAwait(false);

		if (user is null || user.LocalLoginEnabled == enabled)
		{
			return;
		}

		user.LocalLoginEnabled = enabled;
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task<string> CreateTokenAsync(Guid userId, string provider, string purpose)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false)
			?? throw new InvalidOperationException("Usuário inexistente ao gerar token de credencial.");

		return await userManager.GenerateUserTokenAsync(user, provider, purpose).ConfigureAwait(false);
	}

	private async Task<CredentialAccount?> ToAccountAsync(User? user, CancellationToken cancellationToken) =>
		user is null
			? null
			: new CredentialAccount(
				user.Id,
				user.TenantId,
				user.Email!,
				user.PasswordHash is not null,
				user.LocalLoginEnabled,
				await CanReceiveCredentialMailAsync(user, cancellationToken).ConfigureAwait(false));

	/// <summary>
	/// Conta em condição de receber link: login local ligado, não bloqueada e de tenant ativo.
	/// São as mesmas condições do login — um link que revive conta desativada seria uma porta
	/// paralela à desativação (ADR-0032).
	/// </summary>
	private async Task<bool> CanReceiveCredentialMailAsync(User user, CancellationToken cancellationToken)
	{
		if (!user.LocalLoginEnabled)
		{
			return false;
		}

		if (user.LockoutEnabled && user.LockoutEnd is { } end && end > DateTimeOffset.UtcNow)
		{
			return false;
		}

		return await context.Tenants
			.AnyAsync(tenant => tenant.Id == user.TenantId && tenant.IsActive, cancellationToken)
			.ConfigureAwait(false);
	}
}

/// <summary>Nomes e propósitos dos provedores de token registrados pela Api (ADR-0033).</summary>
/// <param name="Invite">Nome do provedor de convite.</param>
/// <param name="InvitePurpose">Propósito do token de convite.</param>
/// <param name="Reset">Nome do provedor de redefinição.</param>
/// <param name="ResetPurpose">Propósito do token de redefinição.</param>
public sealed record CredentialTokenProviderNames(string Invite, string InvitePurpose, string Reset, string ResetPurpose);
