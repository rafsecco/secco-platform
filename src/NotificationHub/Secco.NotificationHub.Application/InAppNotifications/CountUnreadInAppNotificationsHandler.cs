namespace Secco.NotificationHub.Application.InAppNotifications;

/// <summary>
/// Conta o inbox não lido de um usuário — endpoint separado da listagem (o sino do
/// header só precisa do número na maioria das renderizações, não a lista completa).
/// </summary>
public sealed class CountUnreadInAppNotificationsHandler(IInAppNotificationRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="userId">Dono dos itens.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public Task<int> HandleAsync(Guid userId, CancellationToken cancellationToken = default) =>
        repository.CountUnreadByUserAsync(userId, cancellationToken);
}
