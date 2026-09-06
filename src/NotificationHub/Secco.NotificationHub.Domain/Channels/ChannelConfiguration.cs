using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.NotificationHub.Domain.Channels;

/// <summary>
/// Destino configurado de um canal externo, no banco do próprio tenant (ADR-0029).
/// </summary>
/// <remarks>
/// O tenant não é atributo da entidade: o isolamento é físico, por banco (ADR-0005). Foi o que
/// decidiu contra configurar canal por instalação — numa instalação multi-tenant, destino
/// compartilhado significa notificação de um tenant chegando ao canal de outro.
/// <para>
/// <see cref="Destination"/> é <b>segredo</b>: no Teams é a URL do Workflow e no Slack a URL do
/// incoming webhook, e em ambos a própria URL autoriza quem a possui. Fica cifrada em repouso
/// (ADR-0025, cifrador do SDK) e nunca volta em resposta da API.
/// </para>
/// </remarks>
public sealed class ChannelConfiguration : BaseEntity
{
	/// <summary>Tamanho máximo aceito para o identificador de canal.</summary>
	public const int ChannelMaxLength = 50;

	/// <summary>Tamanho máximo aceito para o destino em claro.</summary>
	public const int DestinationMaxLength = 2_048;

	private ChannelConfiguration()
	{
		// Construtor de rehidratação do EF Core
		Channel = string.Empty;
		Destination = string.Empty;
	}

	/// <summary>Configura o destino de um canal para este tenant.</summary>
	/// <param name="channel">Canal (<c>teams</c> ou <c>slack</c>). Obrigatório.</param>
	/// <param name="destination">URL de destino. Obrigatória, tratada como segredo.</param>
	/// <param name="enabled">Se o canal está ativo.</param>
	/// <exception cref="DomainInvariantException">Se canal ou destino forem nulos/vazios.</exception>
	public ChannelConfiguration(string channel, string destination, bool enabled = true)
	{
		if (string.IsNullOrWhiteSpace(channel))
		{
			throw new DomainInvariantException("Uma configuração de canal exige o canal.");
		}

		if (string.IsNullOrWhiteSpace(destination))
		{
			throw new DomainInvariantException("Uma configuração de canal exige o destino.");
		}

		Channel = channel.Trim().ToLowerInvariant();
		Destination = destination;
		Enabled = enabled;
		CreatedAt = DateTimeOffset.UtcNow;
		UpdatedAt = CreatedAt;
	}

	/// <summary>Canal configurado (coluna <c>ds_channel</c>).</summary>
	public string Channel { get; private set; }

	/// <summary>
	/// URL de destino (coluna <c>ds_destination</c>). <b>Segredo</b> — cifrada em repouso,
	/// nunca logada, nunca devolvida pela API.
	/// </summary>
	public string Destination { get; private set; }

	/// <summary>Se o canal está ativo (coluna <c>fl_enabled</c>).</summary>
	public bool Enabled { get; private set; }

	/// <summary>Momento do cadastro (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Momento da última alteração (coluna <c>dt_updated_at</c>).</summary>
	public DateTimeOffset UpdatedAt { get; private set; }

	/// <summary>Substitui o destino e o estado — a rotação de URL é um novo PUT.</summary>
	/// <param name="destination">Novo destino. Obrigatório.</param>
	/// <param name="enabled">Novo estado.</param>
	/// <exception cref="DomainInvariantException">Se o destino for nulo/vazio.</exception>
	public void Update(string destination, bool enabled)
	{
		if (string.IsNullOrWhiteSpace(destination))
		{
			throw new DomainInvariantException("Uma configuração de canal exige o destino.");
		}

		Destination = destination;
		Enabled = enabled;
		UpdatedAt = DateTimeOffset.UtcNow;
	}
}
