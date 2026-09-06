using System.Net.Mail;
using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Canais efetivamente solicitados, depois de validados.</summary>
/// <param name="Email">O canal de e-mail foi solicitado.</param>
/// <param name="InApp">O canal de inbox in-app foi solicitado.</param>
/// <param name="External">
/// Canais externos solicitados (ADR-0029), cujo destino vem da configuração do tenant.
/// </param>
public readonly record struct RequestedChannels(
	bool Email,
	bool InApp,
	IReadOnlyList<NotificationChannel> External)
{
	/// <summary>Nenhum canal externo solicitado.</summary>
	public static readonly IReadOnlyList<NotificationChannel> NoExternal = [];
}

/// <summary>
/// Validação de entrada compartilhada entre o despacho unitário e o em lote (ADR-0020).
/// </summary>
/// <remarks>
/// A separação em <b>conteúdo</b> e <b>destino</b> não é estética: é o que o lote precisa.
/// Numa publicação para muitas pessoas o conteúdo é um só e os destinos são muitos, então o
/// conteúdo é validado uma vez e cada destino é validado por si. Sem essa divisão, o lote
/// duplicaria a regra — e regra duplicada diverge.
/// </remarks>
public static class NotificationInputRules
{
	/// <summary>Valida os canais e o conteúdo compartilhado da notificação.</summary>
	/// <param name="channels">Canais solicitados.</param>
	/// <param name="title">Título.</param>
	/// <param name="message">Mensagem.</param>
	/// <param name="source">Origem (texto livre).</param>
	/// <param name="type">Tipo (texto livre).</param>
	/// <param name="link">Link do item in-app.</param>
	/// <param name="options">Limites configurados.</param>
	public static Result<RequestedChannels> ValidateContent(
		IReadOnlyCollection<string>? channels,
		string? title,
		string? message,
		string? source,
		string? type,
		string? link,
		NotificationHubOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (channels is not { Count: > 0 })
		{
			return Result.Failure<RequestedChannels>(NotificationHubErrors.Notifications.ChannelsRequired);
		}

		var wantsEmail = false;
		var wantsInApp = false;
		var external = new List<NotificationChannel>();

		foreach (var channel in channels)
		{
			if (string.Equals(channel, NotificationHubChannels.Email, StringComparison.OrdinalIgnoreCase))
			{
				wantsEmail = true;
			}
			else if (string.Equals(channel, NotificationHubChannels.InApp, StringComparison.OrdinalIgnoreCase))
			{
				wantsInApp = true;
			}
			else if (string.Equals(channel, NotificationHubChannels.Teams, StringComparison.OrdinalIgnoreCase))
			{
				external.Add(NotificationChannel.Teams);
			}
			else if (string.Equals(channel, NotificationHubChannels.Slack, StringComparison.OrdinalIgnoreCase))
			{
				external.Add(NotificationChannel.Slack);
			}
			else
			{
				return Result.Failure<RequestedChannels>(
					NotificationHubErrors.Notifications.ChannelUnsupported(channel));
			}
		}

		if (string.IsNullOrWhiteSpace(title))
		{
			return Result.Failure<RequestedChannels>(NotificationHubErrors.Notifications.TitleRequired);
		}

		if (title.Length > options.MaxTitleLength)
		{
			return Result.Failure<RequestedChannels>(
				NotificationHubErrors.Notifications.TitleTooLong(options.MaxTitleLength));
		}

		if (string.IsNullOrWhiteSpace(message))
		{
			return Result.Failure<RequestedChannels>(NotificationHubErrors.Notifications.MessageRequired);
		}

		if (message.Length > options.MaxMessageLength)
		{
			return Result.Failure<RequestedChannels>(
				NotificationHubErrors.Notifications.MessageTooLong(options.MaxMessageLength));
		}

		// A constante do domínio é o teto da coluna: a configuração pode APERTAR o limite, nunca
		// afrouxá-lo. Sem esse Math.Min, um MaxSourceLength configurado acima da coluna passaria
		// na validação e estouraria só no INSERT, virando 500 em vez de erro de validação.
		var sourceLimit = Math.Min(options.MaxSourceLength, Notification.SourceMaxLength);

		if (source?.Length > sourceLimit)
		{
			return Result.Failure<RequestedChannels>(
				NotificationHubErrors.Notifications.SourceTooLong(sourceLimit));
		}

		var typeLimit = Math.Min(options.MaxTypeLength, Notification.TypeMaxLength);

		if (type?.Length > typeLimit)
		{
			return Result.Failure<RequestedChannels>(
				NotificationHubErrors.Notifications.TypeTooLong(typeLimit));
		}

		if (link?.Length > options.MaxLinkLength)
		{
			return Result.Failure<RequestedChannels>(
				NotificationHubErrors.Notifications.LinkTooLong(options.MaxLinkLength));
		}

		return Result.Success(new RequestedChannels(wantsEmail, wantsInApp, external));
	}

	/// <summary>Valida um destino contra os canais solicitados.</summary>
	/// <param name="recipient">E-mail do destinatário, exigido pelo canal de e-mail.</param>
	/// <param name="userId">Dono do item de inbox, exigido pelo canal in-app.</param>
	/// <param name="requested">Canais já validados.</param>
	/// <param name="options">Limites configurados.</param>
	public static Result ValidateDestination(
		string? recipient,
		Guid? userId,
		RequestedChannels requested,
		NotificationHubOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (requested.Email)
		{
			if (string.IsNullOrWhiteSpace(recipient))
			{
				return Result.Failure(NotificationHubErrors.Notifications.RecipientRequired);
			}

			if (recipient.Length > options.MaxRecipientLength)
			{
				return Result.Failure(
					NotificationHubErrors.Notifications.RecipientTooLong(options.MaxRecipientLength));
			}

			if (!MailAddress.TryCreate(recipient, out _))
			{
				return Result.Failure(NotificationHubErrors.Notifications.RecipientInvalid);
			}
		}

		if (requested.InApp && (userId is null || userId == Guid.Empty))
		{
			return Result.Failure(NotificationHubErrors.Notifications.UserIdRequired);
		}

		return Result.Success();
	}
}
