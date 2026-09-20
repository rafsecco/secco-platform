using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Envia (ou reenvia) o convite para a pessoa definir a primeira senha (ADR-0033).
/// </summary>
/// <remarks>
/// Recusa conta que já tem senha: a partir daí o caminho é redefinição, não convite — e um
/// convite aceito depois da senha definida seria uma segunda porta para a mesma conta.
/// <para>
/// Repetir a chamada manda outro e-mail, de propósito (ADR-0034): o endpoint existe pelo efeito
/// externo. O que não muda é o estado — os links são da mesma conta e morrem juntos no primeiro uso.
/// </para>
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
public sealed class InviteUserHandler(ICredentialTokens tokens, ICredentialMailer mailer, ICredentialAuditor auditor)
{
	/// <summary>Envia o convite de um usuário do tenant informado.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		// Usuário de outro tenant responde como inexistente: a rota é por tenant e a resposta não
		// pode confirmar a existência de conta alheia.
		if (account is null || account.TenantId != tenantId)
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		if (!account.LocalLoginEnabled)
		{
			return Result.Failure(SecureGateErrors.Credentials.LocalLoginDisabled);
		}

		if (account.HasPassword)
		{
			return Result.Failure(SecureGateErrors.Credentials.AlreadyHasPassword);
		}

		await SendAsync(account, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}

	/// <summary>
	/// Envia o convite de uma conta já resolvida — usado por quem acabou de criá-la ou de religar
	/// o login local, sem repetir a consulta nem as checagens que aquele fluxo já fez.
	/// </summary>
	/// <param name="account">Conta destino.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task SendAsync(CredentialAccount account, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(account);

		var token = await tokens.CreateInviteTokenAsync(account.UserId, cancellationToken).ConfigureAwait(false);

		await mailer.SendInviteAsync(account.Email, account.UserId, token, cancellationToken).ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.InviteSent, account.UserId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);
	}
}
