using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.NotificationHub.Domain.Notifications;

/// <summary>
/// Uma entrega por canal externo: o chamador já montou o conteúdo pronto — este produto só
/// enfileira, envia e rastreia o status (BaseEntity Guid v7, ADR-0017).
/// </summary>
/// <remarks>
/// Era específica de e-mail até a ADR-0029, que a generalizou com <see cref="Channel"/>. O
/// critério foi o mesmo que a Fase 8.4 usou para <b>separar</b> o <c>InAppNotification</c>:
/// ciclo de vida. Lá era diferente (lido/não lido, sem entrega), e por isso separou; aqui
/// e-mail, Teams e Slack têm o <b>mesmo</b> ciclo — pendente, enviado ou falho, com retry por
/// entrega —, e o mesmo critério manda juntar.
/// <para>
/// <see cref="Recipient"/> é o endereço no canal de e-mail e fica <b>nulo</b> nos canais
/// externos, cujo destino vem da configuração do tenant e nunca do registro (ADR-0029).
/// </para>
/// </remarks>
public sealed class Notification : BaseEntity
{
	/// <summary>Tamanho máximo aceito para a origem (coluna <c>ds_source</c>).</summary>
	public const int SourceMaxLength = 128;

	/// <summary>Tamanho máximo aceito para o tipo (coluna <c>ds_type</c>).</summary>
	public const int TypeMaxLength = 128;

	private Notification()
	{
		// Construtor de rehidratação do EF Core
		Subject = string.Empty;
		Body = string.Empty;
	}

	/// <summary>Cria uma entrega pendente por e-mail.</summary>
	/// <param name="recipient">E-mail do destinatário, já resolvido pelo chamador. Obrigatório.</param>
	/// <param name="subject">Assunto pronto. Obrigatório.</param>
	/// <param name="body">Corpo pronto (texto ou HTML). Obrigatório.</param>
	/// <param name="source">Origem, texto livre (o Hub nunca interpreta). Opcional.</param>
	/// <param name="type">Tipo, texto livre (o Hub nunca interpreta). Opcional.</param>
	/// <param name="scheduledFor">Instante da entrega. Nulo = imediata. Opcional.</param>
	/// <exception cref="DomainInvariantException">Se destinatário, assunto ou corpo forem nulos/vazios.</exception>
	public Notification(
		string recipient, string subject, string body, string? source = null, string? type = null,
		DateTimeOffset? scheduledFor = null)
		: this(NotificationChannel.Email, recipient, subject, body, source, type, scheduledFor)
	{
	}

	/// <summary>Cria uma entrega pendente por canal externo, cujo destino vem da configuração.</summary>
	/// <param name="channel">Canal da entrega.</param>
	/// <param name="subject">Assunto/título pronto. Obrigatório.</param>
	/// <param name="body">Corpo pronto. Obrigatório.</param>
	/// <param name="source">Origem, texto livre (o Hub nunca interpreta). Opcional.</param>
	/// <param name="type">Tipo, texto livre (o Hub nunca interpreta). Opcional.</param>
	/// <param name="scheduledFor">Instante da entrega. Nulo = imediata. Opcional.</param>
	/// <exception cref="DomainInvariantException">Se assunto ou corpo forem nulos/vazios.</exception>
	public static Notification ForExternalChannel(
		NotificationChannel channel, string subject, string body, string? source = null, string? type = null,
		DateTimeOffset? scheduledFor = null) =>
		new(channel, recipient: null, subject, body, source, type, scheduledFor);

	private Notification(
		NotificationChannel channel, string? recipient, string subject, string body, string? source, string? type,
		DateTimeOffset? scheduledFor)
	{
		// O destinatário é exigido apenas no canal de e-mail: nos externos o destino vem da
		// configuração do tenant, e um endereço no registro seria dado sem significado.
		if (channel == NotificationChannel.Email && string.IsNullOrWhiteSpace(recipient))
		{
			throw new DomainInvariantException("Uma notificação de e-mail exige destinatário não vazio.");
		}

		if (string.IsNullOrWhiteSpace(subject))
		{
			throw new DomainInvariantException("Uma notificação exige assunto não vazio.");
		}

		if (string.IsNullOrWhiteSpace(body))
		{
			throw new DomainInvariantException("Uma notificação exige corpo não vazio.");
		}

		Channel = channel;
		Recipient = recipient;
		Subject = subject;
		Body = body;
		Source = source;
		Type = type;
		ScheduledFor = scheduledFor;
		Status = NotificationStatus.Pending;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Canal da entrega (coluna <c>ie_channel</c>).</summary>
	public NotificationChannel Channel { get; private set; }

	/// <summary>
	/// E-mail do destinatário (coluna <c>ds_recipient</c>). Nulo em canal externo, cujo destino
	/// vem da configuração do tenant.
	/// </summary>
	public string? Recipient { get; private set; }

	/// <summary>Assunto (coluna <c>ds_subject</c>).</summary>
	public string Subject { get; private set; }

	/// <summary>Corpo (coluna <c>ds_body</c>).</summary>
	public string Body { get; private set; }

	/// <summary>Origem, texto livre (coluna <c>ds_source</c>).</summary>
	public string? Source { get; private set; }

	/// <summary>Tipo, texto livre (coluna <c>ds_type</c>).</summary>
	public string? Type { get; private set; }

	/// <summary>Estado do envio (coluna <c>ie_status</c>).</summary>
	public NotificationStatus Status { get; private set; }

	/// <summary>Motivo da falha, quando <see cref="Status"/> é <see cref="NotificationStatus.Failed"/> (coluna <c>ds_failure_reason</c>).</summary>
	public string? FailureReason { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>
	/// Instante em que a entrega deve ser enfileirada para envio (coluna <c>dt_scheduled_for</c>).
	/// Nulo significa entrega imediata.
	/// </summary>
	public DateTimeOffset? ScheduledFor { get; private set; }

	/// <summary>Momento do envio bem-sucedido, quando houver (coluna <c>dt_sent_at</c>).</summary>
	public DateTimeOffset? SentAt { get; private set; }

	/// <summary>Marca a notificação como enviada com sucesso.</summary>
	public void MarkAsSent()
	{
		Status = NotificationStatus.Sent;
		SentAt = DateTimeOffset.UtcNow;
		FailureReason = null;
	}

	/// <summary>Marca a notificação como falha definitiva (todas as tentativas de retry esgotadas).</summary>
	/// <param name="reason">Motivo da falha — nunca a exceção crua (ADR-0020: sem stack trace/detalhe de infraestrutura).</param>
	public void MarkAsFailed(string reason)
	{
		if (string.IsNullOrWhiteSpace(reason))
		{
			throw new DomainInvariantException("O motivo da falha não pode ser vazio.");
		}

		Status = NotificationStatus.Failed;
		FailureReason = reason;
	}
}
