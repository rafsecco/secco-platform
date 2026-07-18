using Secco.NotificationHub.Domain.InAppNotifications;

namespace Secco.NotificationHub.Application.InAppNotifications;

/// <summary>Porta de persistência do inbox in-app — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IInAppNotificationRepository
{
    /// <summary>Persiste um item novo.</summary>
    /// <param name="notification">Item a persistir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddAsync(InAppNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Busca um item pelo identificador.</summary>
    /// <param name="id">Identificador do item.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<InAppNotification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Lista os itens não lidos de um usuário, mais recentes primeiro.</summary>
    /// <param name="userId">Dono dos itens.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<InAppNotification>> GetUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Conta os itens não lidos de um usuário.</summary>
    /// <param name="userId">Dono dos itens.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<int> CountUnreadByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Persiste as mudanças de estado de um item já existente.</summary>
    /// <param name="notification">Item com estado atualizado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task UpdateAsync(InAppNotification notification, CancellationToken cancellationToken = default);
}
