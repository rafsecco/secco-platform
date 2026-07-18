namespace Secco.NotificationHub.Domain.Notifications;

/// <summary>Estado do envio de uma <see cref="Notification"/> (coluna <c>ie_status</c>).</summary>
public enum NotificationStatus
{
    /// <summary>Enfileirada, aguardando o job de envio.</summary>
    Pending = 0,

    /// <summary>Enviada com sucesso pelo provider.</summary>
    Sent = 1,

    /// <summary>Todas as tentativas de envio falharam.</summary>
    Failed = 2,
}
