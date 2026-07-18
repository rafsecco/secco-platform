namespace Secco.NotificationHub.Application;

/// <summary>
/// Permissões do NotificationHub (ADR-0021, formato canônico <c>recurso:acao</c> do kernel).
/// As constantes vivem no PRODUTO — não no SharedKernel (regra de admissão da ADR-0003).
/// Usadas direto como policy: <c>RequireAuthorization(NotificationHubPermissions.Notifications.Write)</c>.
/// </summary>
public static class NotificationHubPermissions
{
    /// <summary>Permissões de despacho/consulta de notificações (e-mail).</summary>
    public static class Notifications
    {
        /// <summary>Consultar o status de uma notificação.</summary>
        public const string Read = "notifications:read";

        /// <summary>Despachar (enviar) uma notificação para 1+ canais.</summary>
        public const string Write = "notifications:write";
    }

    /// <summary>Permissões do inbox in-app (Fase 8.4).</summary>
    public static class InAppNotifications
    {
        /// <summary>Consultar não lidas / contagem de um usuário.</summary>
        public const string Read = "in-app-notifications:read";

        /// <summary>Marcar um item como lido.</summary>
        public const string Write = "in-app-notifications:write";
    }
}
