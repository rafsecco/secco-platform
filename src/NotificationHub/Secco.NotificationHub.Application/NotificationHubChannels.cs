namespace Secco.NotificationHub.Application;

/// <summary>
/// Canais de entrega reconhecidos. Diferente de <c>Source</c>/<c>Type</c> (texto livre — o Hub
/// nunca interpreta), um canal mapeia direto para um caminho de código que precisa existir aqui
/// dentro; por isso é um conjunto fechado, validado.
/// </summary>
/// <remarks>
/// A ADR-0029 acrescentou <see cref="Teams"/> e <see cref="Slack"/> — e o conjunto **continua
/// fechado**. Canal novo segue exigindo provider próprio no produto e ADR própria. Isso é
/// deliberado: canal barato de acrescentar seria canal genérico por webhook, que é exatamente o
/// que se recusou, porque transformaria o Hub num proxy HTTP dirigido pelo consumidor.
/// </remarks>
public static class NotificationHubChannels
{
	/// <summary>Envio por e-mail (assíncrono, com retry — ADR-0015 Camada 2).</summary>
	public const string Email = "email";

	/// <summary>Item no inbox in-app do usuário.</summary>
	public const string InApp = "in_app";

	/// <summary>Mensagem em canal do Microsoft Teams, via webhook de Workflow (ADR-0029).</summary>
	public const string Teams = "teams";

	/// <summary>Mensagem em canal do Slack, via incoming webhook de um Slack app (ADR-0029).</summary>
	public const string Slack = "slack";

	/// <summary>Todos os canais reconhecidos, para validação de entrada.</summary>
	public static readonly IReadOnlyCollection<string> All = [Email, InApp, Teams, Slack];

	/// <summary>
	/// Canais externos cujo destino vem da configuração do tenant, nunca do payload (ADR-0029).
	/// </summary>
	public static readonly IReadOnlyCollection<string> ExternallyConfigured = [Teams, Slack];

	/// <summary>Indica se o canal tem destino resolvido pela configuração do tenant.</summary>
	/// <param name="channel">Canal a verificar.</param>
	public static bool IsExternallyConfigured(string channel) =>
		ExternallyConfigured.Contains(channel, StringComparer.OrdinalIgnoreCase);
}
