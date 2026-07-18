using Secco.NotificationHub.Domain.Notifications;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Porta de persistência de notificações — sempre no banco do tenant atual (ADR-0005).</summary>
public interface INotificationRepository
{
    /// <summary>Persiste uma notificação nova.</summary>
    /// <param name="notification">Notificação a persistir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

    /// <summary>Busca uma notificação pelo identificador.</summary>
    /// <param name="id">Identificador da notificação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Persiste as mudanças de estado de uma notificação já existente.</summary>
    /// <param name="notification">Notificação com estado atualizado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default);
}
