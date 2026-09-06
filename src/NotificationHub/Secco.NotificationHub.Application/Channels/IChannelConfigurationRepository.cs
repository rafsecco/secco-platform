using Secco.NotificationHub.Domain.Channels;

namespace Secco.NotificationHub.Application.Channels;

/// <summary>
/// Persistência da configuração de canais — sempre no banco do tenant atual (ADR-0005/0029).
/// Não existe caminho para ler a configuração de outro tenant: o isolamento é o próprio banco.
/// </summary>
public interface IChannelConfigurationRepository
{
	/// <summary>Busca a configuração de um canal, se houver.</summary>
	/// <param name="channel">Canal (minúsculo).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<ChannelConfiguration?> GetAsync(string channel, CancellationToken cancellationToken = default);

	/// <summary>Lista as configurações do tenant, ordenadas por canal.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<ChannelConfiguration>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Adiciona uma configuração ao contexto.</summary>
	/// <param name="configuration">Configuração a adicionar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task AddAsync(ChannelConfiguration configuration, CancellationToken cancellationToken = default);

	/// <summary>Remove uma configuração.</summary>
	/// <param name="configuration">Configuração a remover.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task RemoveAsync(ChannelConfiguration configuration, CancellationToken cancellationToken = default);

	/// <summary>Efetiva as alterações pendentes.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
