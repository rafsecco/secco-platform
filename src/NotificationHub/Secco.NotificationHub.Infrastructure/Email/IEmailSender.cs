namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>Porta de envio de e-mail — a única coisa que troca se o provider mudar (SMTP, SendGrid...).</summary>
internal interface IEmailSender
{
    /// <summary>Envia um e-mail. Lança em falha — o chamador (job) decide o retry.</summary>
    /// <param name="recipient">E-mail do destinatário.</param>
    /// <param name="subject">Assunto.</param>
    /// <param name="body">Corpo (texto ou HTML).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken);
}
