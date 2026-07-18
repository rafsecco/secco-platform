namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>
/// Payload do job de envio: só o identificador — o conteúdo já está persistido na
/// notificação (evita duplicar dado sensível na fila do Hangfire).
/// </summary>
/// <param name="NotificationId">Identificador da notificação a enviar.</param>
public sealed record SendEmailPayload(Guid NotificationId);
