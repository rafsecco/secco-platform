using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.ClientCredentials;

namespace Secco.NotificationHub.Client;

/// <summary>Opções de conexão com o NotificationHub.</summary>
public sealed class NotificationHubClientOptions
{
	/// <summary>
	/// Seção de configuração usada para ler as credenciais de autenticação quando não
	/// preenchidas em código (<see cref="AuthorityUrl"/>/<see cref="ClientId"/>/<see cref="ClientSecret"/>);
	/// com fallback automático para <c>Secco:SecureGate</c>.
	/// </summary>
	public const string SectionKey = "Secco:NotificationHub";

	/// <summary>Scope OAuth padrão do produto — audience <c>secco-notificationhub</c>.</summary>
	public const string DefaultScope = "notificationhub";

	/// <summary>URL base da API.</summary>
	public string BaseUrl { get; set; } = string.Empty;

	/// <summary>
	/// URL base do emissor (o SecureGate) para autenticação client credentials. Deixar em
	/// branco para a leitura automática da seção <see cref="SectionKey"/> (com fallback para
	/// <c>Secco:SecureGate</c>) feita por
	/// <see cref="NotificationHubClientServiceCollectionExtensions.AddNotificationHubClient"/>.
	/// </summary>
	public string? AuthorityUrl { get; set; }

	/// <summary>Client id do client OAuth. Mesma regra de fallback de <see cref="AuthorityUrl"/>.</summary>
	public string? ClientId { get; set; }

	/// <summary>Client secret do client OAuth; nunca logado (ADR-0020). Mesma regra de fallback de <see cref="AuthorityUrl"/>.</summary>
	public string? ClientSecret { get; set; }

	/// <summary>Scope solicitado ao emissor. Padrão: <see cref="DefaultScope"/> — o adotante não precisa conhecer o valor.</summary>
	public string Scope { get; set; } = DefaultScope;
}

/// <summary>Composição de DI do client (ADR-0006).</summary>
public static class NotificationHubClientServiceCollectionExtensions
{
	/// <summary>
	/// Registra o <see cref="INotificationHubClient"/> tipado via <c>IHttpClientFactory</c>,
	/// autenticado por client credentials quando as credenciais estiverem configuradas. Com
	/// <c>AddSeccoResilience()</c> no host, o client herda o pipeline de resiliência da
	/// plataforma automaticamente (ADR-0004).
	/// </summary>
	/// <remarks>
	/// Todo endpoint do NotificationHub exige permissão (ADR-0021) — sem handler de
	/// autenticação o client compilava, injetava e tomava 401 na primeira chamada, e o
	/// adotante tinha que remontar a composição do <c>HttpClient</c> à mão (issue #25).
	/// <see cref="NotificationHubClientOptions.BaseUrl"/> segue OBRIGATÓRIA e falha rápido —
	/// erro de setup não pode virar <see cref="NullReferenceException"/> em runtime. As
	/// credenciais, diferente da URL, são OPCIONAIS: ausentes, nenhum handler é anexado (serve
	/// DEV com um token HS256 emitido fora da plataforma); PARCIAIS, falha rápido em
	/// <see cref="SeccoClientCredentialsOptions.Validate"/> — a mesma disciplina de
	/// <c>SecureGateClientCredentialsOptions</c> (ADR-0020).
	/// </remarks>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configure">Configuração da conexão (URL base obrigatória; credenciais opcionais).</param>
	public static IServiceCollection AddNotificationHubClient(
		this IServiceCollection services,
		Action<NotificationHubClientOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configure);

		var options = new NotificationHubClientOptions();
		configure(options);

		if (string.IsNullOrWhiteSpace(options.BaseUrl))
		{
			throw new InvalidOperationException("NotificationHubClientOptions.BaseUrl é obrigatória.");
		}

		// Bind lazy da seção Secco:NotificationHub (com fallback para Secco:SecureGate) para
		// quem não preencheu as credenciais em código acima — a maioria dos adotantes.
		services.AddSeccoClientCredentialsOptions(NotificationHubClientOptions.SectionKey);

		// Store PRÓPRIO deste client: pede token com o scope do NotificationHub, nunca
		// compartilhado com outro recurso (least privilege, ADR-0020).
		var tokenStore = new SeccoAccessTokenStore();

		services.AddHttpClient<INotificationHubClient, NotificationHubClient>(client =>
				client.BaseAddress = new Uri(options.BaseUrl))
			.AddSeccoClientCredentials(
				options.Scope,
				tokenStore,
				resolve: serviceProvider => ResolveCredentials(options, serviceProvider));

		return services;
	}

	/// <summary>
	/// Credenciais definidas em código (<paramref name="options"/>) vencem as lidas da seção de
	/// configuração — a leitura automática (com fallback para <c>Secco:SecureGate</c>) só entra
	/// em jogo quando nada foi preenchido manualmente.
	/// </summary>
	private static SeccoClientCredentialsOptions ResolveCredentials(
		NotificationHubClientOptions options, IServiceProvider serviceProvider)
	{
		if (!string.IsNullOrWhiteSpace(options.AuthorityUrl)
			|| !string.IsNullOrWhiteSpace(options.ClientId)
			|| !string.IsNullOrWhiteSpace(options.ClientSecret))
		{
			return new SeccoClientCredentialsOptions
			{
				SectionKey = NotificationHubClientOptions.SectionKey,
				AuthorityUrl = options.AuthorityUrl,
				ClientId = options.ClientId,
				ClientSecret = options.ClientSecret,
			};
		}

		return serviceProvider.GetRequiredKeyedService<SeccoClientCredentialsOptions>(NotificationHubClientOptions.SectionKey);
	}
}
