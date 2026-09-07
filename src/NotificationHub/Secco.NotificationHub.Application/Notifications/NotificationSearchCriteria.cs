using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Pagination;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Filtros da busca de notificações. Todos opcionais; combinados com AND.</summary>
/// <param name="From">Criadas a partir deste momento (inclusive).</param>
/// <param name="To">Criadas até este momento (inclusive).</param>
/// <param name="Status">Estado do envio exato.</param>
/// <param name="Channel">Canal da entrega exato.</param>
/// <param name="Source">Origem, texto livre — igualdade exata (nunca <c>LIKE</c>).</param>
/// <param name="Type">Tipo, texto livre — igualdade exata (nunca <c>LIKE</c>).</param>
/// <param name="ScheduledFrom">
/// Agendadas a partir deste momento (inclusive). Filtra <see cref="Notification.ScheduledFor"/> —
/// diferente de <paramref name="From"/>/<paramref name="To"/>, que filtram a criação.
/// </param>
/// <param name="ScheduledTo">
/// Agendadas até este momento (inclusive). Filtra <see cref="Notification.ScheduledFor"/> —
/// diferente de <paramref name="From"/>/<paramref name="To"/>, que filtram a criação.
/// </param>
/// <param name="Page">Paginação (1-based, normalizada pelo <see cref="PageRequest"/>).</param>
public sealed record NotificationSearchCriteria(
	DateTimeOffset? From = null,
	DateTimeOffset? To = null,
	NotificationStatus? Status = null,
	NotificationChannel? Channel = null,
	string? Source = null,
	string? Type = null,
	DateTimeOffset? ScheduledFrom = null,
	DateTimeOffset? ScheduledTo = null,
	PageRequest? Page = null)
{
	/// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
	public PageRequest EffectivePage => Page ?? PageRequest.Default;
}
