namespace Secco.NotificationHub.Application.InAppNotifications;

/// <summary>Lista o inbox não lido de um usuário, do banco do tenant atual.</summary>
public sealed class GetUnreadInAppNotificationsHandler(IInAppNotificationRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="userId">Dono dos itens.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<IReadOnlyList<InAppNotificationDto>> HandleAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var items = await repository.GetUnreadByUserAsync(userId, cancellationToken).ConfigureAwait(false);

        return items.Select(InAppNotificationDto.FromEntity).ToList();
    }
}
