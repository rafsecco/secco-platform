using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Text;

namespace Secco.SDK.Email;

/// <summary>Envio via SMTP (MailKit) — a implementação padrão de <see cref="ISeccoEmailSender"/>.</summary>
/// <param name="options">Configuração de e-mail.</param>
public sealed class SeccoSmtpEmailSender(SeccoEmailOptions options) : ISeccoEmailSender
{
	/// <inheritdoc />
	public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
	{
		var message = new MimeMessage();
		message.From.Add(new MailboxAddress(options.FromName ?? options.FromAddress, options.FromAddress));
		message.To.Add(MailboxAddress.Parse(recipient));
		message.Subject = subject;
		message.Body = new TextPart(TextFormat.Plain) { Text = body };

		using var client = new SmtpClient();

		await client.ConnectAsync(
			options.Host,
			options.Port,
			options.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
			cancellationToken).ConfigureAwait(false);

		if (!string.IsNullOrEmpty(options.Username))
		{
			await client.AuthenticateAsync(options.Username, options.Password ?? string.Empty, cancellationToken).ConfigureAwait(false);
		}

		await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
		await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
	}
}
