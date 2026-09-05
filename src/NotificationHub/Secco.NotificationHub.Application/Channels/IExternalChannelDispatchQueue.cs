namespace Secco.NotificationHub.Application.Channels;

/// <summary>
/// Porta de despacho assíncrono da entrega em canal externo (ADR-0029) — irmã da
/// <c>IEmailDispatchQueue</c>. A API responde após persistir a entrega como <c>Pending</c>,
/// sem esperar a ferramenta externa: a disponibilidade do Teams ou do Slack não pode fazer
/// parte da disponibilidade da API.
/// </summary>
public interface IExternalChannelDispatchQueue
{
	/// <summary>Enfileira a entrega de uma notificação já persistida.</summary>
	/// <param name="notificationId">Identificador da entrega.</param>
	void Enqueue(Guid notificationId);
}
