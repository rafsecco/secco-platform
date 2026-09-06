using System.Net.Http.Json;
using Secco.NotificationHub.Domain.Notifications;

namespace Secco.NotificationHub.Infrastructure.Channels;

/// <summary>
/// Entrega no Microsoft Teams via webhook de <b>Workflow</b> (Power Automate).
/// </summary>
/// <remarks>
/// Não é escolha entre webhook clássico e Workflow: os Office 365 Connectors foram desativados
/// entre 18 e 22 de maio de 2026 e webhooks de connector deixaram de funcionar. O caminho atual
/// da Microsoft é o gatilho <i>When a Teams webhook request is received</i>, que aceita Adaptive
/// Card e posta com a identidade do Flow bot (ADR-0029).
/// <para>
/// A URL do Workflow tem <b>dono</b> e pode ficar órfã se a pessoa sair da organização. Isso não
/// tem solução aqui: o POST continua respondendo e a entrega para em silêncio do lado do Teams.
/// A mitigação é operacional — configurar coproprietário — e está no README do produto.
/// </para>
/// </remarks>
internal sealed class TeamsNotificationProvider(IHttpClientFactory httpClientFactory) : IExternalChannelProvider
{
	/// <summary>Nome do <c>HttpClient</c> nomeado, que herda a resiliência do SDK.</summary>
	internal const string HttpClientName = "Secco.NotificationHub.Teams";

	/// <inheritdoc />
	public NotificationChannel Channel => NotificationChannel.Teams;

	/// <inheritdoc />
	public async Task SendAsync(
		string destination, string subject, string body, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destination);

		using var client = httpClientFactory.CreateClient(HttpClientName);

		var payload = BuildAdaptiveCard(subject, body);

		using var response = await client.PostAsJsonAsync(destination, payload, cancellationToken)
			.ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			// O corpo da resposta NÃO entra na mensagem (ADR-0020): pode ecoar o payload, e o
			// payload é o conteúdo da notificação.
			throw new HttpRequestException(
				$"O Teams recusou a entrega (HTTP {(int)response.StatusCode}).");
		}
	}

	/// <summary>
	/// Monta o envelope de Adaptive Card que o gatilho de Workflow espera. É esta tradução que
	/// justifica um provider por ferramenta em vez de um webhook genérico.
	/// </summary>
	/// <param name="subject">Título, que vira o bloco em destaque.</param>
	/// <param name="body">Corpo, que vira o bloco de texto com quebra de linha.</param>
	internal static object BuildAdaptiveCard(string subject, string body) => new
	{
		type = "message",
		attachments = new object[]
		{
			new
			{
				contentType = "application/vnd.microsoft.card.adaptive",
				contentUrl = (string?)null,
				content = new
				{
					type = "AdaptiveCard",
					schema = "http://adaptivecards.io/schemas/adaptive-card.json",
					version = "1.4",
					body = new object[]
					{
						new { type = "TextBlock", text = subject, weight = "Bolder", size = "Medium", wrap = true },
						new { type = "TextBlock", text = body, wrap = true },
					},
				},
			},
		},
	};
}
