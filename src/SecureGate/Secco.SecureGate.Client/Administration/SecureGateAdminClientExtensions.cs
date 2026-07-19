using Microsoft.Extensions.DependencyInjection;
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
        var tokenStore = new SecureGateAccessTokenStore();

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

                    handlers.Add(new SecureGateClientCredentialsHandler(options, tokenStore, AdminScope));
                }
            });

        return services;
    }
}
