using Microsoft.Extensions.DependencyInjection;

namespace Secco.NotificationHub.Client;

/// <summary>Opções de conexão com o NotificationHub.</summary>
public sealed class NotificationHubClientOptions
{
    /// <summary>URL base da API.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Composição de DI do client (ADR-0006).</summary>
public static class NotificationHubClientServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="INotificationHubClient"/> tipado via <c>IHttpClientFactory</c>.
    /// Com <c>AddSeccoResilience()</c> no host, o client herda o pipeline de resiliência
    /// da plataforma automaticamente (ADR-0004).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configure">Configuração da conexão (URL base).</param>
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

        services.AddHttpClient<INotificationHubClient, NotificationHubClient>(client =>
            client.BaseAddress = new Uri(options.BaseUrl));

        return services;
    }
}
