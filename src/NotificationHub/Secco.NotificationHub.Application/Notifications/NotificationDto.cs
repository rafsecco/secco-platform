using Secco.NotificationHub.Domain.Notifications;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>
/// Representação de leitura de uma notificação — a entidade nunca cruza a borda HTTP.
/// O corpo/assunto não voltam aqui de propósito: a consulta é para status de entrega,
/// não para reler o conteúdo enviado.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Recipient">Destinatário.</param>
/// <param name="Subject">Assunto.</param>
/// <param name="Status">Estado do envio.</param>
/// <param name="FailureReason">Motivo da falha, quando houver.</param>
/// <param name="CreatedAt">Momento da criação.</param>
/// <param name="SentAt">Momento do envio bem-sucedido, quando houver.</param>
public sealed record NotificationDto(
    Guid Id,
    string Recipient,
    string Subject,
    NotificationStatus Status,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    public static NotificationDto FromEntity(Notification entity) =>
        new(entity.Id, entity.Recipient, entity.Subject, entity.Status, entity.FailureReason, entity.CreatedAt, entity.SentAt);
}
