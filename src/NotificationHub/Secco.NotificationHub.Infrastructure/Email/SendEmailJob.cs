using Secco.NotificationHub.Application.Notifications;
using Secco.SDK.AspNetCore.BackgroundJobs;

namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>
/// Job de envio de e-mail (ADR-0015 Camada 2): busca a notificação, tenta enviar e
/// atualiza o status. Falha marca <c>Failed</c> com o motivo e RELANÇA a exceção — quem
/// decide o retry é o Hangfire (<c>TenantJobRunner</c> no SDK); uma tentativa seguinte
/// bem-sucedida sobrescreve o status de volta para <c>Sent</c>. Enquanto há retry
/// pendente, o status pode aparecer como <c>Failed</c> transitoriamente — simplificação
/// consciente do v1 (documentada no README do produto).
/// </summary>
internal sealed class SendEmailJob(INotificationRepository repository, IEmailSender emailSender)
    : IBackgroundJob<SendEmailPayload>
{
    /// <summary>Tamanho máximo do motivo de falha persistido (ADR-0020: mensagens de exceção não têm teto natural).</summary>
    private const int MaxFailureReasonLength = 500;

    public async Task ExecuteAsync(SendEmailPayload payload, CancellationToken cancellationToken)
    {
        var notification = await repository.GetByIdAsync(payload.NotificationId, cancellationToken).ConfigureAwait(false);

        if (notification is null)
        {
            // Não deveria ocorrer: a notificação é criada antes de enfileirar o job.
            return;
        }

        try
        {
            await emailSender.SendAsync(notification.Recipient, notification.Subject, notification.Body, cancellationToken)
                .ConfigureAwait(false);

            notification.MarkAsSent();
        }
        catch (Exception ex)
        {
            var reason = ex.Message.Length > MaxFailureReasonLength
                ? ex.Message[..MaxFailureReasonLength]
                : ex.Message;

            notification.MarkAsFailed(reason);
            await repository.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);

            throw;
        }

        await repository.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);
    }
}
