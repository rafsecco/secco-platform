using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.SecureGate.Client.Catalog;

/// <summary>Composição de DI do catálogo remoto de tenants (Fase 6.3, ADR-0005/0006).</summary>
public static class SecureGateTenantCatalogExtensions
{
    /// <summary>
    /// Registra o catálogo de tenants servido pelo SecureGate como <see cref="ITenantCatalog"/>
    /// do produto. A decisão é <b>lazy e por configuração</b> (seção <c>Secco:SecureGate</c>):
    /// com a seção presente, o catálogo remoto assume (BaseUrl/ClientId/ClientSecret/Product
    /// obrigatórios — parcial falha rápido); sem ela, o produto segue no catálogo por
    /// configuração do SDK (<see cref="ConfigurationTenantCatalog"/>) — o comportamento de
    /// DEV não muda. Chamar após <c>AddSeccoPlatform()</c>.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSecureGateTenantCatalog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Bind lazy (as options do host de teste chegam depois do Program executar)
        services.TryAddSingleton(serviceProvider =>
        {
            var options = new SecureGateTenantCatalogOptions();
            serviceProvider.GetRequiredService<IConfiguration>()
                .GetSection(SecureGateTenantCatalogOptions.SectionKey)
                .Bind(options);

            if (options.IsConfigured)
            {
                options.Validate();
            }

            return options;
        });

        services.TryAddSingleton<SecureGateAccessTokenStore>();
        services.TryAddTransient<SecureGateClientCredentialsHandler>();

        // Com AddSeccoResilience() no host, o pipeline padrão da plataforma se aplica também
        // aqui (inclusive à aquisição de token, que atravessa o mesmo handler primário)
        services.AddHttpClient(SecureGateTenantCatalog.HttpClientName)
            .ConfigureHttpClient((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<SecureGateTenantCatalogOptions>();

                if (options.IsConfigured)
                {
                    client.BaseAddress = new Uri(options.BaseUrl!, UriKind.Absolute);
                }
            })
            .AddHttpMessageHandler<SecureGateClientCredentialsHandler>();

        // Substitui o registro do SDK (TryAdd lá, Replace aqui — independente da ordem)
        services.RemoveAll<ITenantCatalog>();
        services.AddSingleton<ITenantCatalog>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<SecureGateTenantCatalogOptions>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();

            return options.IsConfigured
                ? new SecureGateTenantCatalog(
                    serviceProvider.GetRequiredService<IHttpClientFactory>(),
                    options,
                    serviceProvider.GetRequiredService<ILogger<SecureGateTenantCatalog>>())
                : new ConfigurationTenantCatalog(configuration);
        });

        return services;
    }
}
