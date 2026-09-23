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

	/// <summary>Link de confirmação da troca de e-mail, enviado ao endereço NOVO.</summary>
	/// <param name="newRecipient">Endereço novo — o único que recebe o link.</param>
	/// <param name="userId">Usuário do link.</param>
	/// <param name="newEmail">Endereço novo, que também viaja no token.</param>
	/// <param name="token">Token da troca.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendEmailChangeConfirmationAsync(
		string newRecipient,
		Guid userId,
		string newEmail,
		string token,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Aviso ao endereço ATUAL de que pediram a troca. <b>Sem link</b>: um aviso com link seria um
	/// segundo alvo de phishing, e o destino aparece mascarado porque quem não tem a caixa nova
	/// não precisa saber qual é.
	/// </summary>
	/// <param name="currentRecipient">Endereço atual da conta.</param>
	/// <param name="newEmail">Endereço pretendido, que o adaptador mascara.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendEmailChangeNoticeAsync(string currentRecipient, string newEmail, CancellationToken cancellationToken = default);

	/// <summary>
	/// Aviso de que o segundo fator foi ligado ou desligado. Sem link e sem código: o e-mail
	/// existe para o dono perceber uma mudança que não foi ele.
	/// </summary>
	/// <param name="recipient">E-mail do dono da conta.</param>
	/// <param name="enabled"><c>true</c> quando o segundo fator passou a valer.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendTwoFactorChangedNoticeAsync(string recipient, bool enabled, CancellationToken cancellationToken = default);

	/// <summary>
	/// Aviso de que a senha mudou. Não leva link nem token: serve para o dono perceber uma troca
	/// que não foi ele, e um link aqui seria mais uma chance de phishing.
	/// </summary>
	/// <param name="recipient">E-mail do destinatário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendPasswordChangedNoticeAsync(string recipient, CancellationToken cancellationToken = default);
}
