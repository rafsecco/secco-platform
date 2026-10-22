using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.ClientCredentials;
using Secco.SecureGate.Client.Catalog;

namespace Secco.SecureGate.Client.Administration;

/// <summary>
/// Composição de DI do <see cref="ISecureGateClient"/> autenticado para uso administrativo
/// (gestão de tenants/roles/usuários) por produtos da plataforma — diferente do catálogo
/// (Fase 6.3) e da resolução de permissões (Fase 6.4), que consomem endpoints específicos
/// internamente; aqui o produto recebe o client tipado completo, gerado por NSwag, para
/// chamar a API administrativa do SecureGate diretamente.
/// </summary>
public static class SecureGateAdminClientExtensions
{
	/// <summary>
	/// Scope solicitado ao SecureGate para as requisições deste client. Espelha
	/// <c>SecureGateScopes.Admin</c>, definido na Application do produto consumidor — o
	/// Client não referencia a Application (ADR-0006), por isso o valor é duplicado aqui
	/// como literal. É um scope AMPLO (gestão de tenants/roles/usuários, não uma operação
	/// específica); a ADR-0020 (least privilege) recomenda reavaliar um scope mais granular
	/// caso o consumo deste client cresça além de um uso administrativo pontual.
	/// </summary>
	private const string AdminScope = "securegate:admin";

	/// <summary>
	/// Registra o <see cref="ISecureGateClient"/> (implementação <see cref="SecureGateClient"/>,
	/// gerado por NSwag) autenticado por client credentials com o scope <c>securegate:admin</c>.
	/// A composição é <b>lazy e por configuração</b> (seção <c>Secco:SecureGate</c>): as
	/// opções são lidas apenas quando o <c>IHttpClientFactory</c> monta o pipeline do
	/// client, e a validação (<see cref="SecureGateClientCredentialsOptions.Validate"/>)
	/// só dispara nesse momento — configuração parcial nunca é ignorada silenciosamente
	/// (ADR-0020); com a seção ausente, o client fica sem <c>BaseAddress</c> nem handler
	/// de autenticação e falhará ao primeiro uso com erro de requisição HTTP comum, não
	/// de configuração. Chamar após <c>AddSeccoPlatform()</c>.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddSecureGateAdminClient(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSecureGateClientCredentialsOptions();

		// Store próprio: o token de administração não se mistura com o do catálogo nem com
		// o da resolução de permissões — um store por recurso/scope (least privilege)
		var tokenStore = new SeccoAccessTokenStore();

		services.AddHttpClient<ISecureGateClient, SecureGateClient>()
			.ConfigureHttpClient((serviceProvider, client) =>
			{
				var options = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

				if (options.IsConfigured)
				{
					client.BaseAddress = new Uri(options.BaseUrl!, UriKind.Absolute);
				}
			})
			.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
			{
				var options = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

				if (options.IsConfigured)
				{
					// Fail-fast: configuração parcial não pode virar um client sem autenticação
					options.Validate(requireProduct: false);

					handlers.Add(new SeccoClientCredentialsHandler(
						options.BaseUrl!, options.ClientId!, options.ClientSecret!, AdminScope, tokenStore));
				}
			});

		return services;
	}

	/// <summary>Scope da gestão do próprio tenant por máquina (ADR-0052). Literal pelo mesmo motivo de <see cref="AdminScope"/>.</summary>
	private const string TenantManageScope = "securegate:tenant-manage";

	/// <summary>
	/// Registra o <see cref="ISecureGateClient"/> autenticado com o scope <c>securegate:tenant-manage</c> (ADR-0052): o
	/// client alcança só pessoas e perfis do tenant do próprio token, nunca a instalação. Credencial de um client de
	/// PRODUTO, criado pelo operador no tenant da aplicação, na subseção <c>Secco:SecureGate:TenantManagement</c>; a URL
	/// base vem de <c>Secco:SecureGate:BaseUrl</c>. Mesma composição lazy de <see cref="AddSecureGateAdminClient"/> —
	/// configuração parcial falha no primeiro uso, nunca vira client sem autenticação — e é alternativa a ele: os dois
	/// registram o mesmo <see cref="ISecureGateClient"/>, então a aplicação chama um só.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	public static IServiceCollection AddSecureGateTenantManagementClient(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.AddSecureGateClientCredentialsOptions();

		// Store próprio, como no administrativo: um store por recurso/scope (least privilege)
		var tokenStore = new SeccoAccessTokenStore();

		services.AddHttpClient<ISecureGateClient, SecureGateClient>()
			.ConfigureHttpClient((serviceProvider, client) =>
			{
				var platform = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

				if (!string.IsNullOrWhiteSpace(platform.BaseUrl))
				{
					client.BaseAddress = new Uri(platform.BaseUrl, UriKind.Absolute);
				}
			})
			.ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
			{
				var baseUrl = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>().BaseUrl;
				var tenantManagement = new SecureGateTenantManagementOptions();
				serviceProvider.GetRequiredService<IConfiguration>()
					.GetSection(SecureGateTenantManagementOptions.SectionKey)
					.Bind(tenantManagement);

				if (string.IsNullOrWhiteSpace(baseUrl)
					&& string.IsNullOrWhiteSpace(tenantManagement.ClientId)
					&& string.IsNullOrWhiteSpace(tenantManagement.ClientSecret))
				{
					return;
				}

				// Fail-fast: com qualquer chave presente, todas são obrigatórias — inclusive com a seção pai completa
				// para o client de plataforma, a subseção faltando não pode virar client sem autenticação
				ValidateTenantManagement(baseUrl, tenantManagement);

				handlers.Add(new SeccoClientCredentialsHandler(
					baseUrl!, tenantManagement.ClientId!, tenantManagement.ClientSecret!, TenantManageScope, tokenStore));
			});

		return services;
	}

	private static void ValidateTenantManagement(string? baseUrl, SecureGateTenantManagementOptions options)
	{
		var missing = new List<string>();

		if (string.IsNullOrWhiteSpace(baseUrl))
		{
			missing.Add($"{SecureGateClientCredentialsOptions.SectionKey}:BaseUrl");
		}

		if (string.IsNullOrWhiteSpace(options.ClientId))
		{
			missing.Add($"{SecureGateTenantManagementOptions.SectionKey}:ClientId");
		}

		if (string.IsNullOrWhiteSpace(options.ClientSecret))
		{
			missing.Add($"{SecureGateTenantManagementOptions.SectionKey}:ClientSecret");
		}

		if (missing.Count > 0)
		{
			throw new InvalidOperationException(
				$"Gestão do tenant no SecureGate parcialmente configurada — faltam: {string.Join(", ", missing)}.");
		}

		if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException(
				$"'{SecureGateClientCredentialsOptions.SectionKey}:BaseUrl' deve ser uma URL http(s) absoluta.");
		}
	}
}
