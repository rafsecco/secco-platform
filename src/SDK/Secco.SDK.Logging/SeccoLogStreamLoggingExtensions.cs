using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.LogStream.Client;
using Secco.SDK.ClientCredentials;
using Secco.SDK.Logging.Internal;

namespace Secco.SDK.Logging;

/// <summary>
/// Composição do sink <c>ILogger</c> → Secco.LogStream (ADR-0008).
/// </summary>
/// <remarks>
/// Não confundir com <c>AddLogStreamClient()</c>, do pacote <c>Secco.LogStream.Client</c>: aquele
/// registra o client HTTP para quem quer <b>consultar</b> ou escrever no LogStream por conta
/// própria; este liga o <c>ILogger&lt;T&gt;</c> que o produto já usa à ingestão, sem o produto
/// escrever uma linha de código de log. Os dois convivem no mesmo host.
/// </remarks>
public static class SeccoLogStreamLoggingExtensions
{
	/// <summary>Nome do <c>HttpClient</c> nomeado usado exclusivamente pelo sink.</summary>
	internal const string HttpClientName = "Secco.SDK.Logging.LogStream";

	/// <summary>
	/// Registra o sink lendo a seção <c>Secco:LogStream</c> da configuração do host.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddLogStream(this IServiceCollection services) =>
		services.AddLogStream(configure: null);

	/// <summary>
	/// Registra o sink permitindo ajustar as opções em código, depois da configuração.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configure">Ajustes aplicados após o bind da seção; opcional.</param>
	public static IServiceCollection AddLogStream(
		this IServiceCollection services,
		Action<LogStreamLoggerOptions>? configure)
	{
		ArgumentNullException.ThrowIfNull(services);

		var optionsBuilder = services
			.AddOptions<LogStreamLoggerOptions>()
			.BindConfiguration(LogStreamLoggerOptions.SectionKey);

		if (configure is not null)
		{
			optionsBuilder.Configure(configure);
		}

		// A conexão com o emissor cai para a seção do SecureGate quando não é declarada aqui:
		// o produto já configurou aquilo para o catálogo e a autorização, e repetir endereço e
		// credencial em duas seções é convite a divergirem.
		services.AddSingleton<IPostConfigureOptions<LogStreamLoggerOptions>, AuthorityFallback>();
		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<IValidateOptions<LogStreamLoggerOptions>, LogStreamLoggerOptionsValidator>());
		optionsBuilder.ValidateOnStart();

		// Store do token vivo fora do pipeline de handlers: o IHttpClientFactory recicla o
		// pipeline periodicamente, e o token não pode morrer junto.
		var tokenStore = new SeccoAccessTokenStore();

		services
			.AddHttpClient(HttpClientName, (provider, client) =>
			{
				var options = provider.GetRequiredService<IOptions<LogStreamLoggerOptions>>().Value;
				client.BaseAddress = new Uri(options.BaseUrl!, UriKind.Absolute);
			})
			// Ordem deliberada: o header de tenant é o handler mais externo, então a requisição
			// de token disparada lá dentro pelo client credentials (que usa base.SendAsync) não
			// carrega X-Tenant-Id — o emissor não tem nada com o tenant do lote.
			.AddHttpMessageHandler(() => new LogStreamTenantHeaderHandler())
			.AddHttpMessageHandler(provider =>
			{
				var options = provider.GetRequiredService<IOptions<LogStreamLoggerOptions>>().Value;

				return new SeccoClientCredentialsHandler(
					options.AuthorityUrl!,
					options.ClientId!,
					options.ClientSecret!,
					options.Scope,
					tokenStore);
			});

		services.TryAddSingleton(provider =>
			new LogStreamLogQueue(
				provider.GetRequiredService<IOptions<LogStreamLoggerOptions>>().Value.QueueCapacity));

		services.TryAddSingleton<ILogStreamIngestionGateway>(provider =>
		{
			var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();

			return new LogStreamIngestionGateway(
				() => new LogStreamClient(httpClientFactory.CreateClient(HttpClientName)));
		});

		services.TryAddEnumerable(
			ServiceDescriptor.Singleton<ILoggerProvider, LogStreamLoggerProvider>());
		services.AddHostedService<LogStreamDispatcher>();

		return services;
	}

	/// <summary>
	/// Preenche emissor e credenciais a partir da seção <c>Secco:SecureGate</c> quando a seção
	/// do sink não os declara.
	/// </summary>
	/// <param name="configuration">Configuração do host.</param>
	private sealed class AuthorityFallback(IConfiguration configuration)
		: IPostConfigureOptions<LogStreamLoggerOptions>
	{
		/// <summary>Chave da seção de conexão com o SecureGate (definida no client daquele produto).</summary>
		private const string SecureGateSection = "Secco:SecureGate";

		/// <inheritdoc />
		public void PostConfigure(string? name, LogStreamLoggerOptions options)
		{
			ArgumentNullException.ThrowIfNull(options);

			options.AuthorityUrl = Fallback(options.AuthorityUrl, "BaseUrl");
			options.ClientId = Fallback(options.ClientId, "ClientId");
			options.ClientSecret = Fallback(options.ClientSecret, "ClientSecret");
		}

		private string? Fallback(string? value, string key) =>
			string.IsNullOrWhiteSpace(value)
				? configuration[$"{SecureGateSection}:{key}"]
				: value;
	}
}
