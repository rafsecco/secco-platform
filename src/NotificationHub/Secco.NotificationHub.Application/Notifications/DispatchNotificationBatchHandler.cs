using Secco.NotificationHub.Application.Channels;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Domain.InAppNotifications;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Um destino do lote: para quem a notificação vai.</summary>
/// <param name="UserId">Dono do item de inbox. Obrigatório se o canal <c>in_app</c> foi solicitado.</param>
/// <param name="Recipient">E-mail do destinatário. Obrigatório se o canal <c>email</c> foi solicitado.</param>
public sealed record NotificationDestination(Guid? UserId, string? Recipient);

/// <summary>Comando de despacho de <b>um conteúdo</b> para <b>muitos destinos</b> (issue #15).</summary>
/// <param name="Title">Título compartilhado. Obrigatório.</param>
/// <param name="Message">Mensagem compartilhada. Obrigatória.</param>
/// <param name="Source">Origem, texto livre (o Hub nunca interpreta). Opcional.</param>
/// <param name="Type">Tipo, texto livre (o Hub nunca interpreta). Opcional.</param>
/// <param name="Link">Link de destino do item in-app, quando houver. Opcional.</param>
/// <param name="Channels">Canais de entrega solicitados, iguais para todos os destinos. Obrigatório.</param>
/// <param name="Destinations">Destinos. Obrigatório, não vazio, limitado por configuração.</param>
public sealed record DispatchNotificationBatchCommand(
	string? Title,
	string? Message,
	string? Source,
	string? Type,
	string? Link,
	IReadOnlyCollection<string>? Channels,
	IReadOnlyList<NotificationDestination>? Destinations);

/// <summary>Resultado do lote: um par de identificadores por destino, na ordem recebida.</summary>
/// <param name="Results">Identificadores criados, posicionalmente alinhados aos destinos enviados.</param>
/// <param name="ExternalNotificationIds">
/// Identificadores das entregas em canal externo — <b>uma por canal</b>, não por destino.
/// </param>
public sealed record DispatchNotificationBatchResult(
	IReadOnlyList<DispatchNotificationResult> Results,
	IReadOnlyList<Guid>? ExternalNotificationIds = null);

/// <summary>
/// Despacha um conteúdo para muitos destinos numa chamada só (issue #15).
/// </summary>
/// <remarks>
/// <b>Por que "um conteúdo, N destinos" e não uma lista de N notificações completas.</b> O caso
/// real é uma publicação que precisa alcançar todo um público — o conteúdo é um só. Repetir
/// título e mensagem por destinatário mandaria o mesmo texto centenas de vezes pela rede sem
/// necessidade. Conteúdo diferente por pessoa continua possível: são chamadas ao endpoint
/// unitário.
/// <para>
/// <b>Validação é tudo-ou-nada.</b> Todos os destinos são validados antes de qualquer escrita,
/// e um destino inválido reprova o lote inteiro apontando a posição. A alternativa — gravar os
/// válidos e reportar os demais — deixaria o chamador sem saber o que repetir, que é justamente
/// a dor descrita na issue ("uma falha no meio deixa parte das pessoas sem aviso").
/// </para>
/// <para>
/// <b>Canal externo no lote entrega UMA vez, não N.</b> O destino de Teams e Slack é a
/// configuração do tenant, não cada destinatário: uma publicação que alcança 500 pessoas por
/// e-mail deve postar <i>uma</i> mensagem no canal da empresa, não quinhentas. Por isso os
/// canais externos criam uma entrega por canal, independentemente do número de destinos.
/// </para>
/// <para>
/// <b>O que ainda é N.</b> As notificações vão ao banco numa ida só (<c>AddRangeAsync</c>), mas
/// o enfileiramento do envio continua sendo um job por destino de e-mail — é o que preserva o
/// retry por notificação que a Fase 8 estabeleceu. Daí o teto de destinos por lote.
/// </para>
/// </remarks>
public sealed class DispatchNotificationBatchHandler(
	INotificationRepository notificationRepository,
	IEmailDispatchQueue emailDispatchQueue,
	IInAppNotificationRepository inAppNotificationRepository,
	IChannelConfigurationRepository channelConfigurationRepository,
	IExternalChannelDispatchQueue externalChannelDispatchQueue,
	NotificationHubOptions options)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de despacho em lote.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<DispatchNotificationBatchResult>> HandleAsync(
		DispatchNotificationBatchCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var content = NotificationInputRules.ValidateContent(
			command.Channels, command.Title, command.Message,
			command.Source, command.Type, command.Link, options);

		if (content.IsFailure)
		{
			return Result.Failure<DispatchNotificationBatchResult>(content.Error);
		}

		if (command.Destinations is not { Count: > 0 })
		{
			return Result.Failure<DispatchNotificationBatchResult>(
				NotificationHubErrors.Notifications.DestinationsRequired);
		}

		if (command.Destinations.Count > options.MaxBatchDestinations)
		{
			return Result.Failure<DispatchNotificationBatchResult>(
				NotificationHubErrors.Notifications.TooManyDestinations(options.MaxBatchDestinations));
		}

		// Valida TODOS antes de escrever qualquer coisa.
		for (var index = 0; index < command.Destinations.Count; index++)
		{
			var destination = command.Destinations[index];
			var validation = NotificationInputRules.ValidateDestination(
				destination.Recipient, destination.UserId, content.Value, options);

			if (validation.IsFailure)
			{
				return Result.Failure<DispatchNotificationBatchResult>(
					NotificationHubErrors.Notifications.DestinationInvalid(index, validation.Error.Description));
			}
		}

		var notifications = new List<Notification>(command.Destinations.Count);
		var inAppNotifications = new List<InAppNotification>(command.Destinations.Count);
		var results = new List<DispatchNotificationResult>(command.Destinations.Count);

		foreach (var destination in command.Destinations)
		{
			Notification? notification = null;
			InAppNotification? inAppNotification = null;

			if (content.Value.Email)
			{
				notification = new Notification(destination.Recipient!, command.Title!, command.Message!);
				notifications.Add(notification);
			}

			if (content.Value.InApp)
			{
				inAppNotification = new InAppNotification(
					destination.UserId!.Value, command.Source, command.Type,
					command.Title!, command.Message!, command.Link);
				inAppNotifications.Add(inAppNotification);
			}

			results.Add(new DispatchNotificationResult(notification?.Id, inAppNotification?.Id));
		}

		if (notifications.Count > 0)
		{
			await notificationRepository.AddRangeAsync(notifications, cancellationToken).ConfigureAwait(false);

			// Só depois de persistido: um job que rode antes do commit não acharia a notificação.
			foreach (var notification in notifications)
			{
				emailDispatchQueue.Enqueue(notification.Id);
			}
		}

		if (inAppNotifications.Count > 0)
		{
			await inAppNotificationRepository.AddRangeAsync(inAppNotifications, cancellationToken)
				.ConfigureAwait(false);
		}

		var externalIds = new List<Guid>(content.Value.External.Count);

		foreach (var channel in content.Value.External)
		{
			var channelName = channel.ToString().ToLowerInvariant();
			var configuration = await channelConfigurationRepository
				.GetAsync(channelName, cancellationToken).ConfigureAwait(false);

			if (configuration is not { Enabled: true })
			{
				return Result.Failure<DispatchNotificationBatchResult>(
					NotificationHubErrors.Channels.NotConfiguredForTenant(channelName));
			}

			var delivery = Notification.ForExternalChannel(channel, command.Title!, command.Message!);

			await notificationRepository.AddAsync(delivery, cancellationToken).ConfigureAwait(false);
			externalChannelDispatchQueue.Enqueue(delivery.Id);

			externalIds.Add(delivery.Id);
		}

		return Result.Success(new DispatchNotificationBatchResult(results, externalIds));
	}
}
