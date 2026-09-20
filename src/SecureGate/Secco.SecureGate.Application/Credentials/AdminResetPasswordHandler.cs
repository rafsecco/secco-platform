using Secco.SecureGate.Application.Sessions;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Redefinição de senha disparada pelo admin (ADR-0033): manda o link para a pessoa e
/// <b>encerra as sessões na hora</b>.
/// </summary>
/// <remarks>
/// A revogação imediata é o ponto: este pedido costuma nascer de suspeita de comprometimento, e
/// esperar a pessoa clicar no link deixaria o invasor dentro nesse intervalo. O admin continua
/// sem saber a senha — ele dispara um link, nunca escolhe uma credencial.
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="revoker">Revogação de sessões (ADR-0032).</param>
public sealed class AdminResetPasswordHandler(
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	ISessionRevoker revoker)
{
	/// <summary>Dispara o link e revoga as sessões do usuário.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		// Usuário de outro tenant responde como inexistente (ADR-0020).
		if (account is null || account.TenantId != tenantId)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		if (!account.LocalLoginEnabled)
		{
			return Result.Failure(SecureGateErrors.Credentials.LocalLoginDisabled);
		}

		// Conta sem senha nunca teve o que redefinir: o link certo é o convite.
		if (account.HasPassword)
		{
			var token = await tokens.CreateResetTokenAsync(userId, cancellationToken).ConfigureAwait(false);
			await mailer.SendResetAsync(account.Email, userId, token, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			var token = await tokens.CreateInviteTokenAsync(userId, cancellationToken).ConfigureAwait(false);
			await mailer.SendInviteAsync(account.Email, userId, token, cancellationToken).ConfigureAwait(false);
		}

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.PasswordChanged, cancellationToken)
			.ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.PasswordReset, account.UserId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
