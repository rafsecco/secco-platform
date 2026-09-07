using Secco.NotificationHub.Domain.Notifications;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Notifications;

/// <summary>Busca paginada de notificações do banco do tenant atual.</summary>
public sealed class SearchNotificationsHandler(INotificationRepository repository, NotificationHubOptions options)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="criteria">Filtros e paginação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<NotificationDto>>> HandleAsync(
		NotificationSearchCriteria criteria,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(criteria);

		if (criteria.From is not null && criteria.To is not null && criteria.From > criteria.To)
		{
			return NotificationHubErrors.Notifications.InvalidDateRange;
		}

		if (criteria.ScheduledFrom is not null && criteria.ScheduledTo is not null
			&& criteria.ScheduledFrom > criteria.ScheduledTo)
		{
			return NotificationHubErrors.Notifications.InvalidDateRange;
		}

		// Filtro é input externo indo para uma cláusula WHERE: sem teto de tamanho é vetor de
		// negação de serviço (ADR-0020). O teto da coluna vence a configuração, como na escrita.
		var sourceLimit = Math.Min(options.MaxSourceLength, Notification.SourceMaxLength);

		if (criteria.Source?.Length > sourceLimit)
		{
			return NotificationHubErrors.Notifications.SourceTooLong(sourceLimit);
		}

		var typeLimit = Math.Min(options.MaxTypeLength, Notification.TypeMaxLength);

		if (criteria.Type?.Length > typeLimit)
		{
			return NotificationHubErrors.Notifications.TypeTooLong(typeLimit);
		}

		var page = await repository.SearchAsync(criteria, cancellationToken).ConfigureAwait(false);

		return PagedResult.Create(
			page.Items.Select(NotificationDto.FromEntity).ToList(),
			new PageRequest(page.Page, page.Size),
			page.TotalCount);
	}
}
