using Secco.NotificationHub.Domain.Notifications;

namespace Secco.NotificationHub.Infrastructure.Channels;

/// <summary>
/// Entrega de uma notificação num canal externo.
/// </summary>
/// <remarks>
/// Uma implementação <b>por ferramenta</b>, cada uma traduzindo o modelo interno para o formato
/// nativo dela — Adaptive Card no Teams, <c>text</c>/Block Kit no Slack. Não existe um
/// <c>WebhookProvider</c> genérico por baixo: a ADR-0029 recusou explicitamente essa abstração,
/// porque ela reduziria os dois a "POST numa URL" e esconderia justamente o que os diferencia.
/// <para>
/// A interface é fina de propósito. O que ela <b>não</b> recebe é tão importante quanto o que
/// recebe: nenhuma URL. O destino é resolvido pelo provider a partir da configuração do tenant.
/// </para>
/// </remarks>
internal interface IExternalChannelProvider
{
	/// <summary>Canal atendido por esta implementação.</summary>
	NotificationChannel Channel { get; }

	/// <summary>
	/// Envia a notificação ao destino configurado. Lança em falha — quem decide o retry é o job
	/// (ADR-0015), como no e-mail.
	/// </summary>
	/// <param name="destination">URL de destino, já decifrada e resolvida pelo chamador.</param>
	/// <param name="subject">Título da mensagem.</param>
	/// <param name="body">Corpo da mensagem.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendAsync(string destination, string subject, string body, CancellationToken cancellationToken);
}
