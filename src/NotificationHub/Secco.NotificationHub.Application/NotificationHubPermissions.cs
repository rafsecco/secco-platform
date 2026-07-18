namespace Secco.NotificationHub.Application;

/// <summary>
/// Permissões do NotificationHub (ADR-0021, formato canônico <c>recurso:acao</c> do kernel).
/// As constantes vivem no PRODUTO — não no SharedKernel (regra de admissão da ADR-0003).
/// Usadas direto como policy: <c>RequireAuthorization(NotificationHubPermissions.Notifications.Write)</c>.
/// </summary>
public static class NotificationHubPermissions
{
    /// <summary>Permissões de notificações.</summary>
    public static class Notifications
    {
        /// <summary>Consultar o status de uma notificação.</summary>
        public const string Read = "notifications:read";

        /// <summary>Enviar (enfileirar) uma notificação.</summary>
        public const string Write = "notifications:write";
    }
}
