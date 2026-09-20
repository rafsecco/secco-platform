using SendGrid;
using SendGrid.Helpers.Mail;

namespace Secco.SDK.Email;

/// <summary>
/// Envio pela API HTTP do SendGrid — o caminho de quem sobe em nuvem sem SMTP.
/// </summary>
/// <remarks>
/// Depende de <see cref="ISendGridClient"/>, não da classe concreta: é o que torna esta
/// implementação testável sem conta no SendGrid e sem rede.
/// <para>
/// Falha lança, como manda o contrato de <see cref="ISeccoEmailSender"/> — quem decide o
/// retry é o chamador. Um status de erro da API vira exceção aqui pelo mesmo motivo.
/// </para>
/// </remarks>
/// <param name="client">Client HTTP do SendGrid.</param>
/// <param name="options">Configuração de e-mail.</param>
public sealed class SeccoSendGridEmailSender(ISendGridClient client, SeccoEmailOptions options)
	: ISeccoEmailSender
{
	/// <inheritdoc />
	public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
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
