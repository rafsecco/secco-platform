using Secco.SecureGate.Application.Sessions;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Confirma a troca de e-mail pelo link enviado ao endereço novo (entrega C).
/// </summary>
/// <remarks>
/// A troca move o <c>SecurityStamp</c>, então as sessões caem (ADR-0032) e os links pendentes de
/// convite e redefinição morrem junto — de graça. A sessão de quem trocou é renovada pela própria
/// página, quando a confirmação acontece no mesmo navegador.
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="revoker">Revogação de sessões (ADR-0032).</param>
public sealed class ConfirmEmailChangeHandler(
	ICredentialTokens tokens,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	/// <summary>Consome o link e troca o e-mail.</summary>
	/// <param name="userId">Usuário do link.</param>
	/// <param name="newEmail">Endereço novo, o mesmo que gerou o token.</param>
	/// <param name="token">Token do link.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<CredentialTokenOutcome> HandleAsync(
		Guid userId,
		string newEmail,
		string token,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null)
		{
			return CredentialTokenOutcome.InvalidToken;
		}

		var outcome = await tokens.ChangeEmailAsync(userId, newEmail, token, cancellationToken).ConfigureAwait(false);

		if (outcome != CredentialTokenOutcome.Done)
		{
			return outcome;
		}

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.EmailChanged, userId, account.TenantId, newEmail, cancellationToken)
			.ConfigureAwait(false);

		return outcome;
	}
}
