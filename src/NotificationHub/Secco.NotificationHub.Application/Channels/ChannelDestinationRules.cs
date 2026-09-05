using System.Net;
using Secco.SharedKernel.Results;

namespace Secco.NotificationHub.Application.Channels;

/// <summary>
/// Validação da URL de destino de um canal externo (ADR-0029/0020).
/// </summary>
/// <remarks>
/// <b>Isto é defesa em profundidade, não a barreira principal.</b> O que de fato impede SSRF é
/// arquitetural e já está decidido: a URL nunca vem do payload da notificação — vem da
/// configuração do tenant, gravada por endpoint com permissão própria. Estas regras existem para
/// o caso de um operador legítimo ser enganado a cadastrar um destino que aponta para dentro da
/// infraestrutura.
/// <para>
/// Por isso a postura é allowlist de esquema e blocklist de faixa reservada, aplicada <b>no
/// cadastro</b> — e não no envio, onde o custo seria pago a cada notificação.
/// </para>
/// </remarks>
public static class ChannelDestinationRules
{
	/// <summary>Valida a URL de destino de um canal externo.</summary>
	/// <param name="destination">URL informada pelo operador.</param>
	public static Result Validate(string? destination)
	{
		if (string.IsNullOrWhiteSpace(destination))
		{
			return Result.Failure(NotificationHubErrors.Channels.DestinationRequired);
		}

		if (destination.Length > Domain.Channels.ChannelConfiguration.DestinationMaxLength)
		{
			return Result.Failure(NotificationHubErrors.Channels.DestinationTooLong(
				Domain.Channels.ChannelConfiguration.DestinationMaxLength));
		}

		if (!Uri.TryCreate(destination, UriKind.Absolute, out var uri))
		{
			return Result.Failure(NotificationHubErrors.Channels.DestinationInvalid);
		}

		// Só HTTPS: o destino é segredo e o corpo carrega conteúdo de notificação.
		if (uri.Scheme != Uri.UriSchemeHttps)
		{
			return Result.Failure(NotificationHubErrors.Channels.DestinationMustBeHttps);
		}

		return IsReservedHost(uri) ? Result.Failure(NotificationHubErrors.Channels.DestinationHostNotAllowed) : Result.Success();
	}

	/// <summary>
	/// Indica se o host aponta para dentro da própria infraestrutura — loopback, faixa privada,
	/// link-local (inclui o endpoint de metadados de nuvem, <c>169.254.169.254</c>) ou nome sem
	/// ponto, que na prática resolve para a rede interna.
	/// </summary>
	/// <param name="uri">URL já validada como absoluta.</param>
	private static bool IsReservedHost(Uri uri)
	{
		if (uri.IsLoopback)
		{
			return true;
		}

		if (IPAddress.TryParse(uri.Host, out var address))
		{
			return IsReservedAddress(address);
		}

		// Nome sem ponto (ex.: "intranet", "localhost") não é host público.
		return !uri.Host.Contains('.', StringComparison.Ordinal);
	}

	private static bool IsReservedAddress(IPAddress address)
	{
		if (IPAddress.IsLoopback(address))
		{
			return true;
		}

		if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
		{
			return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
		}

		var octets = address.GetAddressBytes();

		return octets[0] switch
		{
			10 => true,                                     // 10.0.0.0/8
			127 => true,                                    // 127.0.0.0/8
			169 when octets[1] == 254 => true,              // 169.254.0.0/16 — metadados de nuvem
			172 when octets[1] is >= 16 and <= 31 => true,  // 172.16.0.0/12
			192 when octets[1] == 168 => true,              // 192.168.0.0/16
			0 => true,                                      // 0.0.0.0/8
			_ => false,
		};
	}
}
