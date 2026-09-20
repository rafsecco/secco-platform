namespace Secco.SecureGate.Application.Credentials;

/// <summary>
/// Envio dos e-mails de credencial (ADR-0033). A montagem do link é do adaptador, nunca do caso
/// de uso: é lá que mora a base pública configurada, e é lá que se garante que o header
/// <c>Host</c> jamais participa dela (ADR-0020).
/// </summary>
public interface ICredentialMailer
{
	/// <summary>Convite para definir a primeira senha.</summary>
	/// <param name="recipient">E-mail do destinatário.</param>
	/// <param name="userId">Usuário do link.</param>
	/// <param name="token">Token do convite.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendInviteAsync(string recipient, Guid userId, string token, CancellationToken cancellationToken = default);

	/// <summary>Link de redefinição de senha.</summary>
	/// <param name="recipient">E-mail do destinatário.</param>
	/// <param name="userId">Usuário do link.</param>
	/// <param name="token">Token da redefinição.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendResetAsync(string recipient, Guid userId, string token, CancellationToken cancellationToken = default);

	/// <summary>
	/// Aviso de que a senha mudou. Não leva link nem token: serve para o dono perceber uma troca
	/// que não foi ele, e um link aqui seria mais uma chance de phishing.
	/// </summary>
	/// <param name="recipient">E-mail do destinatário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendPasswordChangedNoticeAsync(string recipient, CancellationToken cancellationToken = default);
}
