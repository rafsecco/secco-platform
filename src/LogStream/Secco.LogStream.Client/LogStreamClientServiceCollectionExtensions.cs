using Microsoft.Extensions.DependencyInjection;

namespace Secco.LogStream.Client;

/// <summary>Opções de conexão com o Secco.LogStream.</summary>
public sealed class LogStreamClientOptions
{
    /// <summary>URL base da API do LogStream.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Composição de DI do client do LogStream (ADR-0006).</summary>
public static class LogStreamClientServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="ILogStreamClient"/> tipado via <c>IHttpClientFactory</c>.
    /// Com <c>AddSeccoResilience()</c> no host, o client herda o pipeline de resiliência
    /// da plataforma automaticamente (ADR-0004).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configure">Configuração da conexão (URL base).</param>
    public static IServiceCollection AddLogStreamClient(
        this IServiceCollection services,
        Action<LogStreamClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new LogStreamClientOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException("LogStreamClientOptions.BaseUrl é obrigatória.");
        }

        services.AddHttpClient<ILogStreamClient, LogStreamClient>(client =>
            client.BaseAddress = new Uri(options.BaseUrl));

        return services;
    }
}
