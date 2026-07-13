using Microsoft.Extensions.DependencyInjection;

namespace Secco.SecureGate.Client;

/// <summary>Opções de conexão com o SecureGate.</summary>
public sealed class SecureGateClientOptions
{
    /// <summary>URL base da API.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Composição de DI do client (ADR-0006).</summary>
public static class SecureGateClientServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="ISecureGateClient"/> tipado via <c>IHttpClientFactory</c>.
    /// Com <c>AddSeccoResilience()</c> no host, o client herda o pipeline de resiliência
    /// da plataforma automaticamente (ADR-0004).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configure">Configuração da conexão (URL base).</param>
    public static IServiceCollection AddSecureGateClient(
        this IServiceCollection services,
        Action<SecureGateClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SecureGateClientOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException("SecureGateClientOptions.BaseUrl é obrigatória.");
        }

        services.AddHttpClient<ISecureGateClient, SecureGateClient>(client =>
            client.BaseAddress = new Uri(options.BaseUrl));

        return services;
    }
}
