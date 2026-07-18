using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application;

/// <summary>Erros de negócio do produto (ADR-0004): códigos estáveis <c>NotificationHub.*</c>.</summary>
public static class NotificationHubErrors
{
    /// <summary>Erros de envio/consulta de notificações (e-mail + in-app, Fase 8.4).</summary>
    public static class Notifications
    {
        /// <summary>Nenhum canal informado.</summary>
        public static readonly Error ChannelsRequired =
            Error.Validation("NotificationHub.Notification.ChannelsRequired", "Ao menos um canal é obrigatório.");

        /// <summary>Canal informado não é reconhecido pelo Hub.</summary>
        public static Error ChannelUnsupported(string channel) =>
            Error.Validation("NotificationHub.Notification.ChannelUnsupported", $"Canal '{channel}' não é reconhecido.");

        /// <summary>Destinatário ausente ou vazio (obrigatório quando o canal e-mail é solicitado).</summary>
        public static readonly Error RecipientRequired =
            Error.Validation("NotificationHub.Notification.RecipientRequired", "O destinatário é obrigatório para o canal e-mail.");

        /// <summary>Destinatário não é um e-mail em formato válido.</summary>
        public static readonly Error RecipientInvalid =
            Error.Validation("NotificationHub.Notification.RecipientInvalid", "O destinatário não é um e-mail válido.");

        /// <summary>Destinatário acima do limite configurado.</summary>
        public static Error RecipientTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.RecipientTooLong", $"O destinatário excede o limite de {limit} caracteres.");

        /// <summary>Dono (userId) ausente (obrigatório quando o canal in-app é solicitado).</summary>
        public static readonly Error UserIdRequired =
            Error.Validation("NotificationHub.Notification.UserIdRequired", "O userId é obrigatório para o canal in-app.");

        /// <summary>Título ausente ou vazio.</summary>
        public static readonly Error TitleRequired =
            Error.Validation("NotificationHub.Notification.TitleRequired", "O título é obrigatório.");

        /// <summary>Título acima do limite configurado.</summary>
        public static Error TitleTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.TitleTooLong", $"O título excede o limite de {limit} caracteres.");

        /// <summary>Mensagem ausente ou vazia.</summary>
        public static readonly Error MessageRequired =
            Error.Validation("NotificationHub.Notification.MessageRequired", "A mensagem é obrigatória.");

        /// <summary>Mensagem acima do limite configurado.</summary>
        public static Error MessageTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.MessageTooLong", $"A mensagem excede o limite de {limit} caracteres.");

        /// <summary>Origem acima do limite configurado.</summary>
        public static Error SourceTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.SourceTooLong", $"A origem excede o limite de {limit} caracteres.");

        /// <summary>Tipo acima do limite configurado.</summary>
        public static Error TypeTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.TypeTooLong", $"O tipo excede o limite de {limit} caracteres.");

        /// <summary>Link acima do limite configurado.</summary>
        public static Error LinkTooLong(int limit) =>
            Error.Validation("NotificationHub.Notification.LinkTooLong", $"O link excede o limite de {limit} caracteres.");

        /// <summary>Notificação por e-mail não encontrada no banco do tenant atual.</summary>
        public static readonly Error NotFound =
            Error.NotFound("NotificationHub.Notification.NotFound", "Notificação não encontrada.");

        /// <summary>Item de inbox in-app não encontrado no banco do tenant atual.</summary>
        public static readonly Error InAppNotFound =
            Error.NotFound("NotificationHub.Notification.InAppNotFound", "Notificação in-app não encontrada.");
    }
}
