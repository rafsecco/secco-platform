using System.Collections.Concurrent;
using Secco.SDK.Email;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>E-mail capturado pelo dublê.</summary>
/// <param name="Recipient">Destinatário.</param>
/// <param name="Subject">Assunto.</param>
/// <param name="Body">Corpo.</param>
public sealed record SentEmail(string Recipient, string Subject, string Body);

/// <summary>
/// Dublê de <see cref="ISeccoEmailSender"/>: guarda o que seria enviado.
/// </summary>
/// <remarks>
/// Registrado na factory base, então vale para todas as suítes — nenhum teste manda e-mail de
/// verdade. Como as factories são compartilhadas por collection, os testes filtram por
/// destinatário (e-mail único por teste) em vez de contar a fila inteira.
/// </remarks>
public sealed class FakeEmailSender : ISeccoEmailSender
{
	private readonly ConcurrentQueue<SentEmail> _sent = new();

	/// <summary>Tudo que foi "enviado" desde que a instância existe.</summary>
	public IReadOnlyList<SentEmail> Sent => [.. _sent];

	/// <summary>E-mails de um destinatário, na ordem de envio.</summary>
	/// <param name="recipient">Destinatário exato.</param>
	public IReadOnlyList<SentEmail> For(string recipient) =>
		[.. _sent.Where(mail => string.Equals(mail.Recipient, recipient, StringComparison.OrdinalIgnoreCase))];

	/// <inheritdoc />
	public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
	{
		_sent.Enqueue(new SentEmail(recipient, subject, body));

		return Task.CompletedTask;
	}
}
