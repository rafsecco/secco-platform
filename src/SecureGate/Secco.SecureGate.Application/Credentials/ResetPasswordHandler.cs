using Secco.SecureGate.Application.Sessions;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Consome o link de redefinição e troca a senha (ADR-0033).
/// </summary>
/// <remarks>
/// Todo evento de senha <b>encerra as sessões abertas</b> pela operação única da ADR-0032: sem
/// isso, quem entrou com a senha antiga — inclusive quem a roubou — continuaria dentro depois da
/// troca, e redefinir senha deixaria de ser a resposta a um comprometimento.
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="revoker">Revogação de sessões (ADR-0032).</param>
public sealed class ResetPasswordHandler(
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	/// <summary>Redefine a senha a partir de um link.</summary>
	/// <param name="userId">Usuário do link.</param>
	/// <param name="token">Token do link.</param>
	/// <param name="invite"><c>true</c> quando o link é de convite (conta que nunca teve senha).</param>
	/// <param name="newPassword">Senha escolhida.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<CredentialTokenOutcome> HandleAsync(
		Guid userId,
		string token,
		bool invite,
		string newPassword,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		var outcome = await tokens
			.SetPasswordAsync(userId, token, invite, newPassword, cancellationToken)
			.ConfigureAwait(false);

		if (outcome != CredentialTokenOutcome.Done)
		{
			if (account is not null && outcome != CredentialTokenOutcome.WeakPassword)
			{
				await auditor.RecordAsync(
					CredentialAuditEvent.LinkRejected, account.UserId, account.TenantId, account.Email, cancellationToken)
					.ConfigureAwait(false);
			}

			return outcome;
		}

		if (account is null)
		{
			return outcome;
		}

		await revoker.RevokeAllAsync(account.UserId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);

		// Aviso ao dono: é assim que uma troca que não foi ele vira um incidente percebido, e não
		// um acesso perdido sem explicação. No convite não há aviso: não havia senha antes, e
		// "sua senha foi alterada" logo após criá-la só confundiria.
		if (!invite)
		{
			await mailer.SendPasswordChangedNoticeAsync(account.Email, cancellationToken).ConfigureAwait(false);
		}

		await auditor.RecordAsync(
			invite ? CredentialAuditEvent.PasswordSet : CredentialAuditEvent.PasswordReset,
			account.UserId,
			account.TenantId,
			account.Email,
			cancellationToken).ConfigureAwait(false);

		return outcome;
	}
}
