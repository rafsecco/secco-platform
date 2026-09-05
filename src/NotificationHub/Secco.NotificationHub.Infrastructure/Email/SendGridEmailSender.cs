using SendGrid;
using SendGrid.Helpers.Mail;

namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>
/// Envio pela API HTTP do SendGrid (issue #14) — o caminho de quem sobe em nuvem sem SMTP.
/// </summary>
/// <remarks>
/// Depende de <see cref="ISendGridClient"/>, não da classe concreta: é o que torna esta
/// implementação testável sem conta no SendGrid e sem rede.
/// <para>
/// Falha lança, como manda o contrato de <see cref="IEmailSender"/> — quem decide o retry é o
/// job (ADR-0015). Um status de erro da API vira exceção aqui pelo mesmo motivo: para o
/// <c>SendEmailJob</c> marcar <c>Failed</c> e o Hangfire repetir.
/// </para>
/// </remarks>
internal sealed class SendGridEmailSender(ISendGridClient client, NotificationHubEmailOptions options)
	: IEmailSender
{
	public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
	{
		var message = MailHelper.CreateSingleEmail(
			new EmailAddress(options.FromAddress, options.FromName),
			new EmailAddress(recipient),
			subject,
			plainTextContent: body,
			htmlContent: null);

		var response = await client.SendEmailAsync(message, cancellationToken).ConfigureAwait(false);

		if ((int)response.StatusCode >= 400)
		{
			// O corpo da resposta NÃO entra na mensagem (ADR-0020): ele pode ecoar o payload,
			// e o payload é o conteúdo da notificação. Só o status, que é o que orienta o retry.
			throw new InvalidOperationException(
				$"O SendGrid recusou o envio (HTTP {(int)response.StatusCode}).");
		}
	}
}
