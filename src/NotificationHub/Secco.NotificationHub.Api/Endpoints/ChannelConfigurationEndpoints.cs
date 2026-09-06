using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.Channels;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.NotificationHub.Api.Endpoints;

/// <summary>
/// Payload de cadastro do destino de um canal externo (ADR-0029).
/// </summary>
/// <remarks>
/// Write-only por design: a URL entra aqui e <b>nunca</b> volta em nenhuma leitura. Quem a
/// possui consegue postar no canal da empresa, então ela tem o mesmo peso de segredo que a
/// connection string do catálogo do SecureGate.
/// </remarks>
/// <param name="Destination">URL de destino. Obrigatória, HTTPS, host público.</param>
/// <param name="Enabled">Se o canal fica ativo. Padrão: ativo.</param>
public sealed record UpsertChannelConfigurationRequest(string? Destination, bool Enabled = true);

/// <summary>
/// Configuração dos canais externos do tenant (<c>/api/v1/channel-configurations</c>, ADR-0029).
/// </summary>
/// <remarks>
/// A existência destes endpoints é o que permite ao contrato de notificação continuar limpo: o
/// consumidor pede um canal e nunca informa URL, e é o Hub que resolve o destino. Sem isto, a
/// alternativa seria aceitar a URL no payload — proxy HTTP dirigido pelo consumidor, SSRF por
/// construção (ADR-0020).
/// </remarks>
public static class ChannelConfigurationEndpoints
{
	/// <summary>Mapeia os endpoints de configuração de canal.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapChannelConfigurationEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/channel-configurations").WithTags("ChannelConfigurations");

		group.MapPut("/{channel}", async (
				string channel,
				UpsertChannelConfigurationRequest request,
				UpsertChannelConfigurationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new UpsertChannelConfigurationCommand(channel, request.Destination, request.Enabled),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.RequireAuthorization(NotificationHubPermissions.ChannelConfigurations.Write)
			.WithName("UpsertChannelConfiguration")
			.WithSummary("Cadastra ou substitui o destino de um canal externo do tenant (idempotente; rotação de URL é um novo PUT).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		group.MapGet("/", async (ListChannelConfigurationsHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(cancellationToken))
				.ToHttpResult(configurations => Results.Ok(configurations)))
			.RequireAuthorization(NotificationHubPermissions.ChannelConfigurations.Read)
			.WithName("ListChannelConfigurations")
			.WithSummary("Lista os canais configurados do tenant. O destino NUNCA é devolvido — a superfície é write-only para o segredo.")
			.Produces<IReadOnlyList<ChannelConfigurationDto>>(StatusCodes.Status200OK);

		group.MapDelete("/{channel}", async (
				string channel,
				DeleteChannelConfigurationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(channel, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.RequireAuthorization(NotificationHubPermissions.ChannelConfigurations.Write)
			.WithName("DeleteChannelConfiguration")
			.WithSummary("Remove o destino configurado de um canal externo do tenant.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}
}
