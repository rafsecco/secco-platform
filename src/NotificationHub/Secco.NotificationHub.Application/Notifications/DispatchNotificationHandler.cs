using System.Net.Mail;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Domain.InAppNotifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Comando de despacho de uma notificação para 1+ canais (Fase 8.4).</summary>
/// <param name="UserId">Dono do item no inbox in-app. Obrigatório se <paramref name="Channels"/> incluir <c>in_app</c>.</param>
/// <param name="Recipient">E-mail do destinatário, já resolvido pelo chamador. Obrigatório se <paramref name="Channels"/> incluir <c>email</c>.</param>
/// <param name="Title">Título pronto. Vira o assunto quando o canal e-mail é solicitado. Obrigatório.</param>
/// <param name="Message">Mensagem pronta. Vira o corpo quando o canal e-mail é solicitado. Obrigatório.</param>
/// <param name="Source">Origem, texto livre (o Hub nunca interpreta). Opcional.</param>
/// <param name="Type">Tipo, texto livre (o Hub nunca interpreta). Opcional.</param>
/// <param name="Link">Link de destino do item in-app, quando houver. Opcional.</param>
/// <param name="Channels">Canais de entrega solicitados (<see cref="NotificationHubChannels"/>). Obrigatório, não vazio.</param>
public sealed record DispatchNotificationCommand(
    Guid? UserId,
    string? Recipient,
    string? Title,
    string? Message,
    string? Source,
    string? Type,
    string? Link,
    IReadOnlyCollection<string>? Channels);

/// <summary>Identificadores dos registros criados pelo despacho, um por canal solicitado.</summary>
/// <param name="EmailNotificationId">Identificador da notificação de e-mail, quando o canal <c>email</c> foi solicitado.</param>
/// <param name="InAppNotificationId">Identificador do item de inbox, quando o canal <c>in_app</c> foi solicitado.</param>
public sealed record DispatchNotificationResult(Guid? EmailNotificationId, Guid? InAppNotificationId);

/// <summary>
/// Valida os limites de entrada (ADR-0020), e para cada canal solicitado cria o registro
/// correspondente: e-mail vira uma <see cref="Notification"/> <c>Pending</c> enfileirada
/// para envio assíncrono (job com retry, ADR-0015); in-app vira um item de inbox gravado
/// de imediato (a "entrega" in-app é a própria escrita no banco, sem fila).
/// </summary>
public sealed class DispatchNotificationHandler(
    INotificationRepository notificationRepository,
    IEmailDispatchQueue emailDispatchQueue,
    IInAppNotificationRepository inAppNotificationRepository,
    NotificationHubOptions options)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de despacho.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<DispatchNotificationResult>> HandleAsync(
        DispatchNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = Validate(command, options);

        if (validation.IsFailure)
        {
            return Result.Failure<DispatchNotificationResult>(validation.Error);
        }

        Guid? emailNotificationId = null;
        Guid? inAppNotificationId = null;

        if (command.Channels!.Contains(NotificationHubChannels.Email, StringComparer.OrdinalIgnoreCase))
        {
            var notification = new Notification(command.Recipient!, command.Title!, command.Message!);

            await notificationRepository.AddAsync(notification, cancellationToken).ConfigureAwait(false);
            emailDispatchQueue.Enqueue(notification.Id);

            emailNotificationId = notification.Id;
        }

        if (command.Channels!.Contains(NotificationHubChannels.InApp, StringComparer.OrdinalIgnoreCase))
        {
            var inAppNotification = new InAppNotification(
                command.UserId!.Value, command.Source, command.Type, command.Title!, command.Message!, command.Link);

            await inAppNotificationRepository.AddAsync(inAppNotification, cancellationToken).ConfigureAwait(false);

            inAppNotificationId = inAppNotification.Id;
        }

        return new DispatchNotificationResult(emailNotificationId, inAppNotificationId);
    }

    private static Result Validate(DispatchNotificationCommand command, NotificationHubOptions options)
    {
        if (command.Channels is not { Count: > 0 })
        {
            return Result.Failure(NotificationHubErrors.Notifications.ChannelsRequired);
        }

        var wantsEmail = false;
        var wantsInApp = false;

        foreach (var channel in command.Channels)
        {
            if (string.Equals(channel, NotificationHubChannels.Email, StringComparison.OrdinalIgnoreCase))
            {
                wantsEmail = true;
            }
            else if (string.Equals(channel, NotificationHubChannels.InApp, StringComparison.OrdinalIgnoreCase))
            {
                wantsInApp = true;
            }
            else
            {
                return Result.Failure(NotificationHubErrors.Notifications.ChannelUnsupported(channel));
            }
        }

        if (wantsEmail)
        {
            if (string.IsNullOrWhiteSpace(command.Recipient))
            {
                return Result.Failure(NotificationHubErrors.Notifications.RecipientRequired);
            }

            if (command.Recipient.Length > options.MaxRecipientLength)
            {
                return Result.Failure(NotificationHubErrors.Notifications.RecipientTooLong(options.MaxRecipientLength));
            }

            if (!MailAddress.TryCreate(command.Recipient, out _))
            {
                return Result.Failure(NotificationHubErrors.Notifications.RecipientInvalid);
            }
        }

        if (wantsInApp && (command.UserId is null || command.UserId == Guid.Empty))
        {
            return Result.Failure(NotificationHubErrors.Notifications.UserIdRequired);
        }

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return Result.Failure(NotificationHubErrors.Notifications.TitleRequired);
        }

        if (command.Title.Length > options.MaxTitleLength)
        {
            return Result.Failure(NotificationHubErrors.Notifications.TitleTooLong(options.MaxTitleLength));
        }

        if (string.IsNullOrWhiteSpace(command.Message))
        {
            return Result.Failure(NotificationHubErrors.Notifications.MessageRequired);
        }

        if (command.Message.Length > options.MaxMessageLength)
        {
            return Result.Failure(NotificationHubErrors.Notifications.MessageTooLong(options.MaxMessageLength));
        }

        if (command.Source?.Length > options.MaxSourceLength)
        {
            return Result.Failure(NotificationHubErrors.Notifications.SourceTooLong(options.MaxSourceLength));
        }

        if (command.Type?.Length > options.MaxTypeLength)
        {
            return Result.Failure(NotificationHubErrors.Notifications.TypeTooLong(options.MaxTypeLength));
        }

        if (command.Link?.Length > options.MaxLinkLength)
        {
            return Result.Failure(NotificationHubErrors.Notifications.LinkTooLong(options.MaxLinkLength));
        }

        return Result.Success();
    }
}
