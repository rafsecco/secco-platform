using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Leitura pontual do status de uma notificação, do banco do tenant atual.</summary>
public sealed class GetNotificationByIdHandler(INotificationRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="id">Identificador da notificação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<NotificationDto>> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return notification is null
            ? NotificationHubErrors.Notifications.NotFound
            : NotificationDto.FromEntity(notification);
    }
}
