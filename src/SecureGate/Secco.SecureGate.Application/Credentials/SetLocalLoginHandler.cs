using Secco.SecureGate.Application.Sessions;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Liga e desliga o login local de uma conta (ADR-0033/ADR-0026).
/// </summary>
/// <remarks>
/// <b>Desligar apaga a senha e revoga as sessões.</b> Sem apagar, a credencial antiga continuaria
/// valendo numa conta que a empresa decidiu ser só do diretório — e o objetivo da mudança seria
/// exatamente esse que ficaria de fora.
/// <para>
/// Efeito colateral só na transição real (ADR-0034): repetir a chamada no mesmo estado não revoga
/// sessão de novo nem manda outro convite.
/// </para>
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="invites">Envio de convite, quando o login local é religado.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="revoker">Revogação de sessões (ADR-0032).</param>
public sealed class SetLocalLoginHandler(
	ICredentialTokens tokens,
	InviteUserHandler invites,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	/// <summary>Aplica o novo estado do login local.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="enabled">Novo estado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		Guid tenantId,
		Guid userId,
		bool enabled,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null || account.TenantId != tenantId)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		// Já está no estado pedido: idempotente e sem efeito colateral nenhum.
		if (account.LocalLoginEnabled == enabled)
		{
			return Result.Success();
		}

		await tokens.SetLocalLoginAsync(userId, enabled, cancellationToken).ConfigureAwait(false);

		if (enabled)
		{
			// A conta volta a aceitar senha, mas continua sem ter uma: quem a define é o dono.
			var reloaded = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

			if (reloaded is { CanReceiveCredentialMail: true, HasPassword: false })
			{
				await invites.SendAsync(reloaded, cancellationToken).ConfigureAwait(false);
			}

			await auditor.RecordAsync(
				CredentialAuditEvent.LocalLoginEnabled, account.UserId, account.TenantId, account.Email, cancellationToken)
				.ConfigureAwait(false);

			return Result.Success();
		}

		await tokens.RemovePasswordAsync(userId, cancellationToken).ConfigureAwait(false);

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.LocalLoginDisabled, account.UserId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
