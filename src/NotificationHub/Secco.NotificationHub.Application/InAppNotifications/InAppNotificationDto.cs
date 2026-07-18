using Secco.NotificationHub.Domain.InAppNotifications;

namespace Secco.NotificationHub.Application.InAppNotifications;

/// <summary>Representação de leitura de um item do inbox in-app — a entidade nunca cruza a borda HTTP.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="UserId">Dono do item.</param>
/// <param name="Source">Origem, texto livre.</param>
/// <param name="Type">Tipo, texto livre.</param>
/// <param name="Title">Título.</param>
/// <param name="Message">Mensagem.</param>
/// <param name="Link">Link de destino, quando houver.</param>
/// <param name="IsRead">Estado de leitura.</param>
/// <param name="CreatedAt">Momento da criação.</param>
/// <param name="ReadAt">Momento em que foi lida, quando houver.</param>
public sealed record InAppNotificationDto(
    Guid Id,
    Guid UserId,
    string Source,
    string Type,
    string Title,
    string Message,
    string? Link,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    public static InAppNotificationDto FromEntity(InAppNotification entity) =>
        new(entity.Id, entity.UserId, entity.Source, entity.Type, entity.Title, entity.Message,
            entity.Link, entity.IsRead, entity.CreatedAt, entity.ReadAt);
}
