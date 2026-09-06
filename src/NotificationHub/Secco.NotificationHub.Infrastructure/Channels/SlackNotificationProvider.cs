using System.Net.Http.Json;
using Secco.NotificationHub.Domain.Notifications;

namespace Secco.NotificationHub.Infrastructure.Channels;

/// <summary>
/// Entrega no Slack via <b>incoming webhook</b> de um Slack app.
/// </summary>
/// <remarks>
/// Ao contrário do Teams, aqui o caminho simples segue vivo: a documentação oficial não marca
/// incoming webhooks como descontinuados. O Slack recomenda <c>chat.postMessage</c> apenas
/// quando é preciso apagar, editar ou rotear dinamicamente entre canais — nada disso está no
/// caso de uso (ADR-0029). Se isso mudar, o caminho correto passa a ser bot token, e a ADR
/// registra esse gatilho.
/// <para>
/// Payload em Block Kit com <c>text</c> de fallback: o <c>text</c> é o que aparece em
/// notificação de push e em cliente que não renderiza blocos, então omiti-lo degradaria
/// justamente o caso "urgente", que é o motivo desta issue existir.
/// </para>
/// </remarks>
internal sealed class SlackNotificationProvider(IHttpClientFactory httpClientFactory) : IExternalChannelProvider
{
	/// <summary>Nome do <c>HttpClient</c> nomeado, que herda a resiliência do SDK.</summary>
	internal const string HttpClientName = "Secco.NotificationHub.Slack";

	/// <inheritdoc />
	public NotificationChannel Channel => NotificationChannel.Slack;

	/// <inheritdoc />
	public async Task SendAsync(
		string destination, string subject, string body, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destination);

		using var client = httpClientFactory.CreateClient(HttpClientName);

		using var response = await client
			.PostAsJsonAsync(destination, BuildMessage(subject, body), cancellationToken)
			.ConfigureAwait(false);

		if (!response.IsSuccessStatusCode)
		{
			// Sem o corpo da resposta na mensagem (ADR-0020).
			throw new HttpRequestException(
				$"O Slack recusou a entrega (HTTP {(int)response.StatusCode}).");
		}
	}

	/// <summary>Monta o payload do Slack: blocos para leitura, <c>text</c> para fallback.</summary>
	/// <param name="subject">Título, que vira o cabeçalho.</param>
	/// <param name="body">Corpo, que vira a seção de texto.</param>
	internal static object BuildMessage(string subject, string body) => new
	{
		text = subject,
		blocks = new object[]
		{
			new { type = "header", text = new { type = "plain_text", text = Truncate(subject, 150), emoji = true } },
			new { type = "section", text = new { type = "mrkdwn", text = body } },
		},
	};

	/// <summary>Corta no limite do bloco de cabeçalho do Slack, que rejeita acima de 150.</summary>
	/// <param name="value">Texto original.</param>
	/// <param name="maxLength">Limite do bloco.</param>
	private static string Truncate(string value, int maxLength) =>
		value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";
}
