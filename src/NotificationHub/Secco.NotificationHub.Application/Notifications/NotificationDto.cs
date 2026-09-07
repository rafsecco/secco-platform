using Secco.NotificationHub.Domain.Notifications;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>
/// Representação de leitura de uma notificação — a entidade nunca cruza a borda HTTP.
/// O corpo/assunto não voltam aqui de propósito: a consulta é para status de entrega,
/// não para reler o conteúdo enviado.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Channel">Canal da entrega (ADR-0029).</param>
/// <param name="Recipient">Destinatário no canal de e-mail; nulo em canal externo, cujo destino vem da configuração do tenant.</param>
/// <param name="Source">Origem, texto livre (o Hub nunca interpreta).</param>
/// <param name="Type">Tipo, texto livre (o Hub nunca interpreta).</param>
/// <param name="Subject">Assunto.</param>
/// <param name="Status">Estado do envio.</param>
/// <param name="FailureReason">Motivo da falha, quando houver.</param>
/// <param name="CreatedAt">Momento da criação.</param>
/// <param name="ScheduledFor">Instante agendado para a entrega, quando houver. Nulo = foi imediata.</param>
/// <param name="SentAt">Momento do envio bem-sucedido, quando houver.</param>
public sealed record NotificationDto(
	Guid Id,
	NotificationChannel Channel,
	string? Recipient,
	string? Source,
	string? Type,
	string Subject,
	NotificationStatus Status,
	string? FailureReason,
	DateTimeOffset CreatedAt,
	DateTimeOffset? ScheduledFor,
	DateTimeOffset? SentAt)
{
	/// <summary>Projeta a entidade para o DTO.</summary>
	public static NotificationDto FromEntity(Notification entity) =>
		new(entity.Id, entity.Channel, entity.Recipient, entity.Source, entity.Type, entity.Subject, entity.Status,
			entity.FailureReason, entity.CreatedAt, entity.ScheduledFor, entity.SentAt);
}
