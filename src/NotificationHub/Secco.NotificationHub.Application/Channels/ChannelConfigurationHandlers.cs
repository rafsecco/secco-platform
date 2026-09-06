using Secco.NotificationHub.Domain.Channels;
using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Channels;

/// <summary>Comando de cadastro/substituição do destino de um canal externo.</summary>
/// <param name="Channel">Canal externo (<c>teams</c> ou <c>slack</c>).</param>
/// <param name="Destination">URL de destino. Tratada como segredo.</param>
/// <param name="Enabled">Se o canal fica ativo.</param>
public sealed record UpsertChannelConfigurationCommand(string? Channel, string? Destination, bool Enabled);

/// <summary>
/// Leitura de uma configuração de canal — <b>sem</b> o destino.
/// </summary>
/// <remarks>
/// A ausência do destino aqui é o ponto, não um esquecimento: a URL é segredo (quem a possui
/// consegue postar no canal da empresa), então a superfície é write-only, como a connection
/// string do catálogo do SecureGate.
/// </remarks>
/// <param name="Channel">Canal configurado.</param>
/// <param name="Enabled">Se está ativo.</param>
/// <param name="CreatedAt">Momento do cadastro.</param>
/// <param name="UpdatedAt">Momento da última alteração.</param>
public sealed record ChannelConfigurationDto(
	string Channel,
	bool Enabled,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt)
{
	/// <summary>Projeta a entidade, deixando o destino de fora de propósito.</summary>
	/// <param name="entity">Configuração persistida.</param>
	public static ChannelConfigurationDto FromEntity(ChannelConfiguration entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new ChannelConfigurationDto(entity.Channel, entity.Enabled, entity.CreatedAt, entity.UpdatedAt);
	}
}

/// <summary>Cadastra ou substitui o destino de um canal externo do tenant (ADR-0029).</summary>
/// <remarks>
/// Idempotente por canal: rotacionar a URL é um novo PUT, no mesmo padrão do banco de tenant
/// no SecureGate. Não existe endpoint que devolva o destino.
/// </remarks>
public sealed class UpsertChannelConfigurationHandler(IChannelConfigurationRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de upsert.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		UpsertChannelConfigurationCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var channel = command.Channel?.Trim().ToLowerInvariant() ?? string.Empty;

		if (!NotificationHubChannels.IsExternallyConfigured(channel))
		{
			return Result.Failure(NotificationHubErrors.Channels.NotConfigurable(channel));
		}

		var destination = ChannelDestinationRules.Validate(command.Destination);

		if (destination.IsFailure)
		{
			return destination;
		}

		var existing = await repository.GetAsync(channel, cancellationToken).ConfigureAwait(false);

		if (existing is null)
		{
			await repository
				.AddAsync(new ChannelConfiguration(channel, command.Destination!, command.Enabled), cancellationToken)
				.ConfigureAwait(false);
		}
		else
		{
			existing.Update(command.Destination!, command.Enabled);
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}

/// <summary>Lista as configurações de canal do tenant, sem os destinos.</summary>
public sealed class ListChannelConfigurationsHandler(IChannelConfigurationRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<ChannelConfigurationDto>>> HandleAsync(
		CancellationToken cancellationToken = default)
	{
		var configurations = await repository.ListAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success<IReadOnlyList<ChannelConfigurationDto>>(
			[.. configurations.Select(ChannelConfigurationDto.FromEntity)]);
	}
}

/// <summary>Remove a configuração de um canal externo do tenant.</summary>
public sealed class DeleteChannelConfigurationHandler(IChannelConfigurationRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="channel">Canal a remover.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(string? channel, CancellationToken cancellationToken = default)
	{
		var normalized = channel?.Trim().ToLowerInvariant() ?? string.Empty;

		if (!NotificationHubChannels.IsExternallyConfigured(normalized))
		{
			return Result.Failure(NotificationHubErrors.Channels.NotConfigurable(normalized));
		}

		var existing = await repository.GetAsync(normalized, cancellationToken).ConfigureAwait(false);

		if (existing is null)
		{
			return Result.Failure(NotificationHubErrors.Channels.NotFound);
		}

		await repository.RemoveAsync(existing, cancellationToken).ConfigureAwait(false);
		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
