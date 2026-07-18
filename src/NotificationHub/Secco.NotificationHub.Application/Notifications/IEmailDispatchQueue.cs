namespace Secco.NotificationHub.Application.Notifications;

/// <summary>
/// Porta de despacho assíncrono do envio de e-mail: a API responde após persistir a
/// notificação como <c>Pending</c>, sem esperar o envio de fato — o job roda em
/// background com retry automático (ADR-0015 Camada 2). A Infrastructure captura o
/// tenant atual no enfileiramento (o job roda fora do request).
/// </summary>
public interface IEmailDispatchQueue
{
    /// <summary>Enfileira o envio de uma notificação já persistida.</summary>
    /// <param name="notificationId">Identificador da notificação a enviar.</param>
    void Enqueue(Guid notificationId);
}
