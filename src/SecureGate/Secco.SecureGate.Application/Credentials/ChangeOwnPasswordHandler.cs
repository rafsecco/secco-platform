using Secco.SecureGate.Application.Sessions;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Troca da senha pelo próprio dono (ADR-0033), exigindo a senha atual.
/// </summary>
/// <remarks>
/// A senha atual não é burocracia: sem ela, uma sessão sequestrada trocaria a credencial e
/// expulsaria o dono da própria conta. Com ela, quem roubou o cookie precisa também saber a senha.
/// <para>
/// Como todo evento de senha, este revoga as sessões (ADR-0032). A sessão de quem trocou continua
/// porque a página renova o cookie logo em seguida — as outras caem.
/// </para>
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="revoker">Revogação de sessões (ADR-0032).</param>
public sealed class ChangeOwnPasswordHandler(
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	/// <summary>Troca a senha do usuário autenticado.</summary>
	/// <param name="userId">Usuário da sessão.</param>
	/// <param name="currentPassword">Senha atual, conferida antes da troca.</param>
	/// <param name="newPassword">Nova senha.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<CredentialTokenOutcome> HandleAsync(
		Guid userId,
		string currentPassword,
		string newPassword,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		// Conta só corporativa (ADR-0026) ou sem senha não tem o que trocar aqui.
		if (account is null || !account.CanReceiveCredentialMail || !account.HasPassword)
		{
			return CredentialTokenOutcome.NotAllowed;
		}

		var outcome = await tokens
			.ChangeOwnPasswordAsync(userId, currentPassword, newPassword, cancellationToken)
			.ConfigureAwait(false);

		if (outcome != CredentialTokenOutcome.Done)
		{
			return outcome;
		}

		await revoker.RevokeAllAsync(account.UserId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);

		await mailer.SendPasswordChangedNoticeAsync(account.Email, cancellationToken).ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.PasswordSet, account.UserId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return outcome;
	}
}
