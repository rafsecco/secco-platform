using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application;

/// <summary>Erros de negócio do produto (ADR-0004): códigos estáveis <c>NotificationHub.*</c>.</summary>
public static class NotificationHubErrors
{
    /// <summary>Erros de envio/consulta de notificações.</summary>
    public static class Notifications
    {
        /// <summary>Destinatário ausente ou vazio.</summary>
        public static readonly Error RecipientRequired =
            Error.Validation("NotificationHub.Notification.RecipientRequired", "O destinatário é obrigatório.");

        /// <summary>Destinatário não é um e-mail em formato válido.</summary>
        public static readonly Error RecipientInvalid =
            Error.Validation("NotificationHub.Notification.RecipientInvalid", "O destinatário não é um e-mail válido.");

        /// <summary>Destinatário acima do limite configurado.</summary>
        public static Error RecipientTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.RecipientTooLong", $"O destinatário excede o limite de {limit} caracteres.");

        /// <summary>Assunto ausente ou vazio.</summary>
        public static readonly Error SubjectRequired =
            Error.Validation("NotificationHub.Notification.SubjectRequired", "O assunto é obrigatório.");

        /// <summary>Assunto acima do limite configurado.</summary>
        public static Error SubjectTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.SubjectTooLong", $"O assunto excede o limite de {limit} caracteres.");

        /// <summary>Corpo ausente ou vazio.</summary>
        public static readonly Error BodyRequired =
            Error.Validation("NotificationHub.Notification.BodyRequired", "O corpo é obrigatório.");

        /// <summary>Corpo acima do limite configurado.</summary>
        public static Error BodyTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.BodyTooLong", $"O corpo excede o limite de {limit} caracteres.");

        /// <summary>Notificação não encontrada no banco do tenant atual.</summary>
        public static readonly Error NotFound =
            Error.NotFound("NotificationHub.Notification.NotFound", "Notificação não encontrada.");
    }
}
