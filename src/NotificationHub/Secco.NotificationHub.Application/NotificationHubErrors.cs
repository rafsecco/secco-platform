using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application;

/// <summary>Erros de negócio do produto (ADR-0004): códigos estáveis <c>NotificationHub.*</c>.</summary>
public static class NotificationHubErrors
{
	/// <summary>Erros de envio/consulta de notificações (e-mail + in-app, Fase 8.4).</summary>
	public static class Notifications
	{
		/// <summary>Data inicial posterior à final na busca.</summary>
		public static readonly Error InvalidDateRange =
			Error.Validation("NotificationHub.Notification.InvalidDateRange", "A data inicial não pode ser posterior à final.");

		/// <summary>Nenhum canal informado.</summary>
		public static readonly Error ChannelsRequired =
			Error.Validation("NotificationHub.Notification.ChannelsRequired", "Ao menos um canal é obrigatório.");

		/// <summary>Lote sem nenhum destino.</summary>
		public static readonly Error DestinationsRequired =
			Error.Validation("NotificationHub.Notification.DestinationsRequired",
				"Um lote exige ao menos um destino.");

		/// <summary>Lote acima do teto configurado.</summary>
		/// <param name="max">Máximo de destinos por lote.</param>
		public static Error TooManyDestinations(int max) =>
			Error.Validation("NotificationHub.Notification.TooManyDestinations",
				$"Um lote aceita no máximo {max} destinos. Divida a publicação em lotes menores.");

		/// <summary>
		/// Um destino do lote não passou na validação. O índice localiza qual, sem ecoar o
		/// valor recebido (ADR-0020).
		/// </summary>
		/// <param name="index">Posição do destino no lote, começando em zero.</param>
		/// <param name="reason">Descrição do problema daquele destino.</param>
		public static Error DestinationInvalid(int index, string reason) =>
			Error.Validation("NotificationHub.Notification.DestinationInvalid",
				$"Destino na posição {index} inválido: {reason}");

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

		/// <summary>Agendamento além do horizonte máximo configurado.</summary>
		public static Error ScheduledTooFarAhead(int days) =>
			Error.Validation("NotificationHub.Notification.ScheduledTooFarAhead", $"A entrega não pode ser agendada para mais de {days} dias à frente.");

		/// <summary>Notificação por e-mail não encontrada no banco do tenant atual.</summary>
		public static readonly Error NotFound =
			Error.NotFound("NotificationHub.Notification.NotFound", "Notificação não encontrada.");

		/// <summary>Item de inbox in-app não encontrado no banco do tenant atual.</summary>
		public static readonly Error InAppNotFound =
			Error.NotFound("NotificationHub.Notification.InAppNotFound", "Notificação in-app não encontrada.");
	}

	/// <summary>Erros de configuração de canal externo (ADR-0029).</summary>
	public static class Channels
	{
		/// <summary>Canal informado não é um canal externo configurável.</summary>
		public static Error NotConfigurable(string channel) =>
			Error.Validation("NotificationHub.Channel.NotConfigurable",
				$"O canal '{channel}' não tem destino configurável. Apenas canais externos ({string.Join(", ", NotificationHubChannels.ExternallyConfigured)}) têm.");

		/// <summary>Configuração não encontrada para o canal neste tenant.</summary>
		public static readonly Error NotFound =
			Error.NotFound("NotificationHub.Channel.NotFound", "Este tenant não tem destino configurado para o canal.");

		/// <summary>Destino ausente.</summary>
		public static readonly Error DestinationRequired =
			Error.Validation("NotificationHub.Channel.DestinationRequired", "O destino do canal é obrigatório.");

		/// <summary>
		/// Destino acima do limite. O valor NÃO entra na mensagem: ele é segredo (ADR-0020).
		/// </summary>
		/// <param name="max">Tamanho máximo aceito.</param>
		public static Error DestinationTooLong(int max) =>
			Error.Validation("NotificationHub.Channel.DestinationTooLong",
				$"O destino do canal excede o limite de {max} caracteres.");

		/// <summary>Destino não é URL absoluta. O valor recebido não é ecoado (ADR-0020).</summary>
		public static readonly Error DestinationInvalid =
			Error.Validation("NotificationHub.Channel.DestinationInvalid", "O destino do canal deve ser uma URL absoluta.");

		/// <summary>Destino em esquema diferente de HTTPS.</summary>
		public static readonly Error DestinationMustBeHttps =
			Error.Validation("NotificationHub.Channel.DestinationMustBeHttps",
				"O destino do canal deve usar HTTPS: a URL é segredo e o corpo carrega o conteúdo da notificação.");

		/// <summary>Destino aponta para faixa reservada — loopback, rede privada ou link-local.</summary>
		public static readonly Error DestinationHostNotAllowed =
			Error.Validation("NotificationHub.Channel.DestinationHostNotAllowed",
				"O destino do canal não pode apontar para loopback, rede privada ou link-local.");

		/// <summary>Canal externo solicitado sem destino configurado ou com o canal desativado.</summary>
		/// <param name="channel">Canal solicitado.</param>
		public static Error NotConfiguredForTenant(string channel) =>
			Error.Validation("NotificationHub.Channel.NotConfiguredForTenant",
				$"O canal '{channel}' não está configurado ou está desativado para este tenant.");
	}
}
