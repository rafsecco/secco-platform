using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Secco.SDK.ClientCredentials;

/// <summary>
/// Composição de DI de credenciais client credentials para clients de produto
/// (<c>Secco.&lt;Produto&gt;.Client</c>). O desenho espelha o catálogo remoto de tenants do
/// <c>Secco.SecureGate.Client</c> (<c>SecureGateTenantCatalogExtensions</c>): bind lazy por
/// configuração e handler anexado ao <c>HttpClient</c> só quando as credenciais estiverem
/// presentes. Pacote fino de propósito: só <c>Microsoft.Extensions.Http</c> e DI/Configuration
/// — nada de Hangfire/JwtBearer/AspNetCore, que o <c>Secco.SDK.AspNetCore</c> arrasta e que um
/// client de produto (inclusive um worker de console) não deveria carregar.
/// </summary>
public static class SeccoClientCredentialsExtensions
{
	/// <summary>Seção de configuração de fallback compartilhada pela plataforma (o SecureGate).</summary>
	private const string SecureGateFallbackSection = "Secco:SecureGate";

	/// <summary>
	/// Registra o bind lazy de <see cref="SeccoClientCredentialsOptions"/> a partir da seção
	/// <paramref name="sectionKey"/>, com fallback para <c>Secco:SecureGate</c> quando a seção
	/// própria não traz uma das três chaves — evita repetir a mesma credencial em cada produto
	/// que já fala com o SecureGate para outra finalidade (catálogo, autorização, logging).
	/// </summary>
	/// <remarks>
	/// Registrado como serviço KEYED pelo próprio <paramref name="sectionKey"/>, e não como
	/// singleton comum: dois clients no mesmo host (ex.: LogStream e NotificationHub) chamam
	/// este método com seções diferentes, e um singleton compartilhado faria o <c>TryAdd</c> do
	/// segundo chamador virar no-op — o segundo client herdaria em silêncio as opções (fallback
	/// já resolvido incluído) do primeiro. A chave de DI evita essa colisão sem exigir um tipo
	/// de opções próprio por produto.
	/// </remarks>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="sectionKey">Seção de configuração do client (ex.: <c>Secco:LogStream</c>).</param>
	public static IServiceCollection AddSeccoClientCredentialsOptions(this IServiceCollection services, string sectionKey)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);

		services.TryAddKeyedSingleton<SeccoClientCredentialsOptions>(sectionKey, (serviceProvider, key) =>
		{
			var configuration = serviceProvider.GetRequiredService<IConfiguration>();
			var resolvedSectionKey = (string)key!;

			var options = new SeccoClientCredentialsOptions { SectionKey = resolvedSectionKey };
			configuration.GetSection(resolvedSectionKey).Bind(options);

			options.AuthorityUrl = Fallback(configuration, options.AuthorityUrl, "BaseUrl");
			options.ClientId = Fallback(configuration, options.ClientId, "ClientId");
			options.ClientSecret = Fallback(configuration, options.ClientSecret, "ClientSecret");

			return options;
		});

		return services;
	}

	/// <summary>
	/// Anexa o <see cref="SeccoClientCredentialsHandler"/> ao pipeline do <c>HttpClient</c>
	/// quando as credenciais resolvidas por <paramref name="resolve"/> estiverem configuradas.
	/// Ausência total de credenciais não anexa handler nenhum (serve DEV com um token HS256
	/// emitido fora da plataforma); presença PARCIAL falha rápido em
	/// <see cref="SeccoClientCredentialsOptions.Validate"/> (ADR-0020) — nunca vira handler
	/// ausente em silêncio.
	/// </summary>
	/// <param name="builder">Builder do <c>HttpClient</c> do client de produto.</param>
	/// <param name="scope">Scope OAuth solicitado ao emissor — o mínimo necessário (least privilege, ADR-0020).</param>
	/// <param name="store">Cache de token PRÓPRIO deste client (least privilege por recurso, ADR-0020).</param>
	/// <param name="resolve">
	/// Resolve as opções a usar — tipicamente o serviço keyed registrado por
	/// <see cref="AddSeccoClientCredentialsOptions"/> para a mesma seção.
	/// </param>
	public static IHttpClientBuilder AddSeccoClientCredentials(
		this IHttpClientBuilder builder,
		string scope,
		SeccoAccessTokenStore store,
		Func<IServiceProvider, SeccoClientCredentialsOptions> resolve)
	{
		ArgumentNullException.ThrowIfNull(builder);
		ArgumentException.ThrowIfNullOrWhiteSpace(scope);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(resolve);

		return builder.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
		{
			var options = resolve(serviceProvider);

			if (!options.IsConfigured)
			{
				return;
			}

			// Fail-fast: configuração parcial não pode virar um client sem autenticação (ADR-0020)
			options.Validate();

			handlers.Add(new SeccoClientCredentialsHandler(
				options.AuthorityUrl!, options.ClientId!, options.ClientSecret!, scope, store));
		});
	}

	private static string? Fallback(IConfiguration configuration, string? value, string key) =>
		string.IsNullOrWhiteSpace(value)
			? configuration[$"{SecureGateFallbackSection}:{key}"]
			: value;
}
