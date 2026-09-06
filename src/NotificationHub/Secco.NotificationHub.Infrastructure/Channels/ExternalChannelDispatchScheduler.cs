using Secco.NotificationHub.Application.Channels;
using Secco.SDK.AspNetCore.BackgroundJobs;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SharedKernel.Exceptions;

namespace Secco.NotificationHub.Infrastructure.Channels;

/// <summary>
/// Adaptador (escopo de request) da porta de despacho externo: captura o tenant atual no
/// enfileiramento — o job roda fora do request e precisa saber em qual banco ler a
/// configuração e gravar o status (ADR-0005/0015).
/// </summary>
internal sealed class ExternalChannelDispatchScheduler(
	IBackgroundJobScheduler scheduler, ITenantContext tenantContext) : IExternalChannelDispatchQueue
{
	public void Enqueue(Guid notificationId)
	{
		if (tenantContext.TenantId is not { } tenantId)
		{
			// A entrega só é criada com o tenant resolvido; chegar aqui sem tenant é bug do chamador.
			throw new DomainInvariantException("Não é possível enfileirar a entrega sem um tenant resolvido.");
		}

		scheduler.Enqueue<SendExternalChannelJob, SendExternalChannelPayload>(
			tenantId, new SendExternalChannelPayload(notificationId));
	}
}
