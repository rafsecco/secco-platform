using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.InAppNotifications;

/// <summary>Marca um item do inbox in-app como lido, no banco do tenant atual.</summary>
public sealed class MarkInAppNotificationAsReadHandler(IInAppNotificationRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="id">Identificador do item.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var notification = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (notification is null)
        {
            return Result.Failure(NotificationHubErrors.Notifications.InAppNotFound);
        }

        notification.MarkAsRead();

        await repository.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
