namespace Secco.SDK.Email;

/// <summary>Porta de envio de e-mail — a única coisa que troca se o provider mudar (SMTP, SendGrid...).</summary>
public interface ISeccoEmailSender
{
	/// <summary>Envia um e-mail. Lança em falha — o chamador decide o retry.</summary>
	/// <param name="recipient">E-mail do destinatário.</param>
	/// <param name="subject">Assunto.</param>
	/// <param name="body">Corpo (texto ou HTML).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);
}
