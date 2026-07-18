using System.Net.Mail;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Comando de envio de uma notificação por e-mail.</summary>
/// <param name="Recipient">E-mail do destinatário, já resolvido pelo chamador. Obrigatório.</param>
/// <param name="Subject">Assunto pronto. Obrigatório.</param>
/// <param name="Body">Corpo pronto (texto ou HTML). Obrigatório.</param>
public sealed record SendNotificationCommand(string? Recipient, string? Subject, string? Body);

/// <summary>
/// Valida os limites de entrada (ADR-0020), persiste a notificação como <c>Pending</c> e
/// enfileira o envio — o envio de fato é assíncrono (job com retry, ADR-0015), por isso o
/// sucesso aqui devolve o registro no estado inicial, não a confirmação de entrega.
/// </summary>
public sealed class SendNotificationHandler(
    INotificationRepository repository,
    IEmailDispatchQueue dispatchQueue,
    NotificationHubOptions options)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de envio.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<NotificationDto>> HandleAsync(SendNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var validation = Validate(command, options);

        if (validation.IsFailure)
        {
            return Result.Failure<NotificationDto>(validation.Error);
        }

        var notification = new Notification(command.Recipient!, command.Subject!, command.Body!);

        await repository.AddAsync(notification, cancellationToken).ConfigureAwait(false);

        dispatchQueue.Enqueue(notification.Id);

        return NotificationDto.FromEntity(notification);
    }

    private static Result Validate(SendNotificationCommand command, NotificationHubOptions options)
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

        if (string.IsNullOrWhiteSpace(command.Subject))
        {
            return Result.Failure(NotificationHubErrors.Notifications.SubjectRequired);
        }

        if (command.Subject.Length > options.MaxSubjectLength)
        {
            return Result.Failure(NotificationHubErrors.Notifications.SubjectTooLong(options.MaxSubjectLength));
        }

        if (string.IsNullOrWhiteSpace(command.Body))
        {
            return Result.Failure(NotificationHubErrors.Notifications.BodyRequired);
        }

        if (command.Body.Length > options.MaxBodyLength)
        {
            return Result.Failure(NotificationHubErrors.Notifications.BodyTooLong(options.MaxBodyLength));
        }

        return Result.Success();
    }
}
