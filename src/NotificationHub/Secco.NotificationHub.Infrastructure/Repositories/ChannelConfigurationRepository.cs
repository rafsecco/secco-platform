using Microsoft.EntityFrameworkCore;
using Secco.NotificationHub.Application.Channels;
using Secco.NotificationHub.Domain.Channels;
using Secco.NotificationHub.Infrastructure.Contexts;

namespace Secco.NotificationHub.Infrastructure.Repositories;

/// <summary>Persistência da configuração de canais no banco do tenant atual.</summary>
internal sealed class ChannelConfigurationRepository(NotificationHubDbContext context)
	: IChannelConfigurationRepository
{
	public async Task<ChannelConfiguration?> GetAsync(string channel, CancellationToken cancellationToken = default) =>
		await context.ChannelConfigurations
			.FirstOrDefaultAsync(configuration => configuration.Channel == channel, cancellationToken)
			.ConfigureAwait(false);

	public async Task<IReadOnlyList<ChannelConfiguration>> ListAsync(CancellationToken cancellationToken = default) =>
		await context.ChannelConfigurations
			.OrderBy(configuration => configuration.Channel)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

	public async Task AddAsync(ChannelConfiguration configuration, CancellationToken cancellationToken = default)
	{
		await context.ChannelConfigurations.AddAsync(configuration, cancellationToken).ConfigureAwait(false);
	}

	public Task RemoveAsync(ChannelConfiguration configuration, CancellationToken cancellationToken = default)
	{
		context.ChannelConfigurations.Remove(configuration);
		return Task.CompletedTask;
	}

	public async Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
		await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
