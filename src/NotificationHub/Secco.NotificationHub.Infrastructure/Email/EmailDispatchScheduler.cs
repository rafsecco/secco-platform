using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SDK.AspNetCore.BackgroundJobs;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Exceptions;

namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>
/// Adaptador (escopo de request) da porta de despacho: captura o tenant atual no
/// enfileiramento — o job roda fora do request e precisa saber em qual banco gravar
/// (ADR-0005/0015).
/// </summary>
internal sealed class EmailDispatchScheduler(IBackgroundJobScheduler scheduler, ITenantContext tenantContext)
    : IEmailDispatchQueue
{
    public void Enqueue(Guid notificationId)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            // A notificação só é criada com o tenant já resolvido (a escrita no banco do
            // tenant já teria falhado antes) — chegar aqui sem tenant é bug do chamador.
            throw new DomainInvariantException("Não é possível enfileirar o envio sem um tenant resolvido.");
        }

        scheduler.Enqueue<SendEmailJob, SendEmailPayload>(tenantId, new SendEmailPayload(notificationId));
    }
}
