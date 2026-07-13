using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Secco.SDK.AspNetCore.Authorization;
using Secco.SecureGate.Client.Catalog;

namespace Secco.SecureGate.Client.Authorization;

/// <summary>Composição de DI da resolução remota de permissões (Fase 6.4, ADR-0021).</summary>
public static class SecureGatePermissionResolverExtensions
{
    /// <summary>
    /// Registra a resolução de permissões servida pelo SecureGate como
    /// <see cref="IPermissionResolver"/> do produto. Mesma decisão lazy do catálogo
    /// (seção <c>Secco:SecureGate</c> — BaseUrl/ClientId/ClientSecret; <c>Product</c> não é
    /// exigido aqui): sem a seção, o resolver por configuração do SDK segue valendo (DEV).
    /// O token usa apenas o scope <c>authorization:read</c> — separado do token do catálogo
    /// (least privilege por recurso). Chamar após <c>AddSeccoPlatform()</c>.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSecureGatePermissionResolver(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSecureGateClientCredentialsOptions();

        // Store próprio: o token de autorização não se mistura com o do catálogo
        var tokenStore = new SecureGateAccessTokenStore();

        services.AddHttpClient(SecureGatePermissionResolver.HttpClientName)
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
                    handlers.Add(new SecureGateClientCredentialsHandler(
                        options, tokenStore, SecureGateClientCredentialsOptions.AuthorizationScope));
                }
            });

        // Substitui o resolver por configuração do SDK (TryAdd lá, Replace aqui)
        services.RemoveAll<IPermissionResolver>();
        services.AddSingleton<IPermissionResolver>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<SecureGateClientCredentialsOptions>();

            if (!options.IsConfigured)
            {
                return new ConfigurationPermissionResolver(serviceProvider.GetRequiredService<IConfiguration>());
            }

            options.Validate(requireProduct: false);

            return new SecureGatePermissionResolver(serviceProvider.GetRequiredService<IHttpClientFactory>());
        });

        return services;
    }
}
