using Secco.NotificationHub.Application.Channels;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SDK.AspNetCore.BackgroundJobs;

namespace Secco.NotificationHub.Infrastructure.Channels;

/// <summary>
/// Payload do job de entrega externa: só o identificador — o conteúdo já está persistido na
/// notificação, e o destino vem da configuração do tenant. Nada de segredo entra na fila.
/// </summary>
/// <param name="NotificationId">Identificador da entrega a enviar.</param>
public sealed record SendExternalChannelPayload(Guid NotificationId);

/// <summary>
/// Job de entrega em canal externo (ADR-0029), irmão do <c>SendEmailJob</c>.
/// </summary>
/// <remarks>
/// É job separado, e não o de e-mail generalizado, por uma razão operacional: jobs já
/// enfileirados no Hangfire referenciam o tipo pelo nome, então renomear ou reassinar o job
/// existente órfãaria o que estivesse em voo no momento do deploy.
/// <para>
/// <b>O destino é resolvido aqui, a partir da configuração do tenant</b> — nunca vem do payload
/// da notificação nem da fila. É a barreira anti-SSRF da ADR-0029, e ela vive neste ponto
/// justamente para que nenhum caminho de chamada consiga contorná-la.
/// </para>
/// <para>
/// Falha marca <c>Failed</c> e <b>relança</b>, como no e-mail: quem decide o retry é o Hangfire,
/// e uma tentativa seguinte bem-sucedida sobrescreve o status para <c>Sent</c>.
/// </para>
/// </remarks>
internal sealed class SendExternalChannelJob(
	INotificationRepository notificationRepository,
	IChannelConfigurationRepository channelConfigurationRepository,
	IEnumerable<IExternalChannelProvider> providers) : IBackgroundJob<SendExternalChannelPayload>
{
	/// <summary>Tamanho máximo do motivo de falha persistido (ADR-0020).</summary>
	private const int MaxFailureReasonLength = 500;

	public async Task ExecuteAsync(SendExternalChannelPayload payload, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(payload);

		var notification = await notificationRepository
			.GetByIdAsync(payload.NotificationId, cancellationToken).ConfigureAwait(false);

		if (notification is null || notification.Channel == NotificationChannel.Email)
		{
			// E-mail tem job próprio; ausência não deveria ocorrer (a entrega é criada antes).
			return;
		}

		var provider = providers.FirstOrDefault(candidate => candidate.Channel == notification.Channel);

		if (provider is null)
		{
			await FailAsync(notification, "canal sem provider registrado neste servidor", cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		var channelName = notification.Channel.ToString().ToLowerInvariant();
		var configuration = await channelConfigurationRepository
			.GetAsync(channelName, cancellationToken).ConfigureAwait(false);

		if (configuration is not { Enabled: true })
		{
			// Config removida ou desativada entre o enfileiramento e a execução. Falha explícita:
			// repetir não resolveria, e um "sucesso" silencioso esconderia notificação não entregue.
			await FailAsync(notification, "canal sem destino configurado ou desativado", cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		try
		{
			await provider
				.SendAsync(configuration.Destination, notification.Subject, notification.Body, cancellationToken)
				.ConfigureAwait(false);

			notification.MarkAsSent();
			await notificationRepository.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception)
		{
			await FailAsync(notification, exception.Message, cancellationToken).ConfigureAwait(false);

			// Relança: o retry é decisão do Hangfire, não deste job (ADR-0015).
			throw;
		}
	}

	private async Task FailAsync(Notification notification, string reason, CancellationToken cancellationToken)
	{
		var truncated = reason.Length > MaxFailureReasonLength ? reason[..MaxFailureReasonLength] : reason;

		notification.MarkAsFailed(truncated);
		await notificationRepository.UpdateAsync(notification, cancellationToken).ConfigureAwait(false);
	}
}
