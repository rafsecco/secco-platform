namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Pedido de troca do próprio e-mail (entrega C).
/// </summary>
/// <remarks>
/// Exigir a senha atual é o que quebra a cadeia "cookie roubado → troca o e-mail → esqueci minha
/// senha na caixa nova". Conta só corporativa não tem senha a exigir, e nela o e-mail já não
/// governa o login: quem governa é o vínculo <c>oid</c> (ADR-0026).
/// <para>
/// O retorno diz apenas se a <b>senha conferiu</b> — e nada sobre o endereço. Livre e em uso
/// produzem o mesmo desfecho visível, porque a alternativa contaria a um usuário autenticado
/// quais endereços existem na instalação (ADR-0020).
/// </para>
/// </remarks>
/// <param name="tokens">Porta de tokens de credencial.</param>
/// <param name="mailer">Porta de envio.</param>
/// <param name="auditor">Trilha (best-effort).</param>
/// <param name="throttle">Limite por conta e por IP, compartilhado com a recuperação de senha.</param>
public sealed class RequestEmailChangeHandler(
	ICredentialTokens tokens,
	ICredentialMailer mailer,
	ICredentialAuditor auditor,
	IPasswordResetThrottle throttle)
{
	/// <summary>Tamanho máximo aceito para o e-mail — o mesmo da criação de usuário.</summary>
	private const int EmailMaxLength = 256;

	/// <summary>Dispara a confirmação, quando houver o que disparar.</summary>
	/// <param name="userId">Usuário autenticado.</param>
	/// <param name="newEmail">Endereço pretendido (não confiável).</param>
	/// <param name="currentPassword">Senha atual, exigida de quem tem senha local.</param>
	/// <param name="remoteAddress">Origem da requisição, para o limite por IP.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> apenas quando a senha atual não confere.</returns>
	public async Task<bool> HandleAsync(
		Guid userId,
		string? newEmail,
		string? currentPassword,
		string? remoteAddress,
		CancellationToken cancellationToken = default)
	{
		var account = await tokens.FindAsync(userId, cancellationToken).ConfigureAwait(false);

		if (account is null || !account.CanReceiveCredentialMail)
		{
			return false;
		}

		if (account.HasPassword
			&& !await tokens.CheckPasswordAsync(userId, currentPassword ?? string.Empty, cancellationToken)
				.ConfigureAwait(false))
		{
			return false;
		}

		var normalized = newEmail?.Trim() ?? string.Empty;

		// Daqui em diante nada é distinguível de fora: formato inválido, endereço igual ao atual,
		// limite estourado e endereço em uso levam todos ao mesmo "pedido aceito".
		if (normalized.Length is 0 or > EmailMaxLength
			|| string.Equals(normalized, account.Email, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		// Mesma cota do "esqueci minha senha": os dois mandam link por e-mail (ADR-0035).
		if (!throttle.TryAcquire(account.Email, remoteAddress))
		{
			return true;
		}

		if (!await tokens.IsEmailAvailableAsync(normalized, cancellationToken).ConfigureAwait(false))
		{
			return true;
		}

		var token = await tokens.CreateEmailChangeTokenAsync(userId, normalized, cancellationToken).ConfigureAwait(false);

		await mailer.SendEmailChangeConfirmationAsync(normalized, userId, normalized, token, cancellationToken)
			.ConfigureAwait(false);
		await mailer.SendEmailChangeNoticeAsync(account.Email, normalized, cancellationToken).ConfigureAwait(false);

		await auditor.RecordAsync(
			CredentialAuditEvent.EmailChangeRequested, userId, account.TenantId, account.Email, cancellationToken)
			.ConfigureAwait(false);

		return true;
	}
}
