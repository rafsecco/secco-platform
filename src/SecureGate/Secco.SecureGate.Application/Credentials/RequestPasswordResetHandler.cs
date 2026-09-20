namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Atende o "esqueci minha senha" (ADR-0033).
/// </summary>
/// <remarks>
/// <b>Não devolve resultado</b>, de propósito: quem chama não tem o que decidir. Conta
/// inexistente, desativada, de tenant inativo, só corporativa ou acima do limite levam todas ao
/// mesmo lugar — nada de retorno para a tela ramificar, porque uma resposta que varia é uma
/// resposta que confirma quais e-mails têm conta (ADR-0020).
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="throttle">Limite por conta e por IP.</param>
public sealed class RequestPasswordResetHandler(
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	IPasswordResetThrottle throttle)
{
	/// <summary>Tamanho máximo aceito para o e-mail — o mesmo da criação de usuário.</summary>
	private const int EmailMaxLength = 256;

	/// <summary>Envia o link se, e somente se, a conta puder recebê-lo.</summary>
	/// <param name="email">E-mail digitado no formulário (não confiável).</param>
	/// <param name="remoteAddress">Origem da requisição, para o limite por IP.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task HandleAsync(string? email, string? remoteAddress, CancellationToken cancellationToken = default)
	{
		var normalized = email?.Trim() ?? string.Empty;

		if (normalized.Length is 0 or > EmailMaxLength)
		{
			return;
		}

		if (!throttle.TryAcquire(normalized, remoteAddress))
		{
			return;
		}

		var account = await tokens.FindByEmailAsync(normalized, cancellationToken).ConfigureAwait(false);

		// Conta inexistente, bloqueada, de tenant inativo ou só corporativa: nada a enviar.
		if (account is null || !account.CanReceiveCredentialMail)
		{
			return;
		}

		// Conta sem senha ainda é convite, não redefinição: o link certo é o que ela já recebeu —
		// e reemiti-lo aqui é o que faz "esqueci minha senha" também resolver convite expirado.
		var token = account.HasPassword
			? await tokens.CreateResetTokenAsync(account.UserId, cancellationToken).ConfigureAwait(false)
			: await tokens.CreateInviteTokenAsync(account.UserId, cancellationToken).ConfigureAwait(false);

		if (account.HasPassword)
		{
			await mailer.SendResetAsync(account.Email, account.UserId, token, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			await mailer.SendInviteAsync(account.Email, account.UserId, token, cancellationToken).ConfigureAwait(false);
		}

		await auditor.RecordAsync(
			CredentialAuditEvent.RecoveryRequested, account.UserId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);
	}
}
