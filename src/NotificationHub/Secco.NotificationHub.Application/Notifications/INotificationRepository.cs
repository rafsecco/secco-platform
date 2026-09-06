using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Pagination;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Porta de persistência de notificações — sempre no banco do tenant atual (ADR-0005).</summary>
public interface INotificationRepository
{
	/// <summary>Persiste uma notificação nova.</summary>
	/// <param name="notification">Notificação a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(Notification notification, CancellationToken cancellationToken = default);

	/// <summary>
	/// Persiste várias notificações em uma única ida ao banco (issue #15).
	/// </summary>
	/// <remarks>
	/// É o que faz o lote valer a pena: <see cref="AddAsync"/> grava uma por chamada, então um
	/// lote em laço trocaria N requisições HTTP por N idas ao banco — quase nada.
	/// </remarks>
	/// <param name="notifications">Notificações a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddRangeAsync(IReadOnlyCollection<Notification> notifications, CancellationToken cancellationToken = default);

	/// <summary>Busca uma notificação pelo identificador.</summary>
	/// <param name="id">Identificador da notificação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

	/// <summary>Persiste as mudanças de estado de uma notificação já existente.</summary>
	/// <param name="notification">Notificação com estado atualizado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task UpdateAsync(Notification notification, CancellationToken cancellationToken = default);

	/// <summary>Busca paginada com os filtros informados, ordenada da mais recente à mais antiga.</summary>
	/// <param name="criteria">Filtros e paginação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PagedResult<Notification>> SearchAsync(NotificationSearchCriteria criteria, CancellationToken cancellationToken = default);
}
