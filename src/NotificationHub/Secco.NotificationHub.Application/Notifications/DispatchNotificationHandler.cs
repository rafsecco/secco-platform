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

		var content = NotificationInputRules.ValidateContent(
			command.Channels, command.Title, command.Message,
			command.Source, command.Type, command.Link, options);

		if (content.IsFailure)
		{
			return Result.Failure<DispatchNotificationResult>(content.Error);
		}

		var destination = NotificationInputRules.ValidateDestination(
			command.Recipient, command.UserId, content.Value, options);

		if (destination.IsFailure)
		{
			return Result.Failure<DispatchNotificationResult>(destination.Error);
		}

		Guid? emailNotificationId = null;
		Guid? inAppNotificationId = null;

		if (content.Value.Email)
		{
			var notification = new Notification(command.Recipient!, command.Title!, command.Message!);

			await notificationRepository.AddAsync(notification, cancellationToken).ConfigureAwait(false);
			emailDispatchQueue.Enqueue(notification.Id);

			emailNotificationId = notification.Id;
		}

		if (content.Value.InApp)
		{
			var inAppNotification = new InAppNotification(
				command.UserId!.Value, command.Source, command.Type, command.Title!, command.Message!, command.Link);

			await inAppNotificationRepository.AddAsync(inAppNotification, cancellationToken).ConfigureAwait(false);

			inAppNotificationId = inAppNotification.Id;
		}

		return new DispatchNotificationResult(emailNotificationId, inAppNotificationId);
	}
}
