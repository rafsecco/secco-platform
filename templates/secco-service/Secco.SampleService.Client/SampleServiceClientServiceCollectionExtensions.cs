using Microsoft.Extensions.DependencyInjection;

namespace Secco.SampleService.Client;

/// <summary>Opções de conexão com o SampleService.</summary>
public sealed class SampleServiceClientOptions
{
    /// <summary>URL base da API.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}

/// <summary>Composição de DI do client (ADR-0006).</summary>
public static class SampleServiceClientServiceCollectionExtensions
{
    /// <summary>
    /// Registra o <see cref="ISampleServiceClient"/> tipado via <c>IHttpClientFactory</c>.
    /// Com <c>AddSeccoResilience()</c> no host, o client herda o pipeline de resiliência
    /// da plataforma automaticamente (ADR-0004).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configure">Configuração da conexão (URL base).</param>
    public static IServiceCollection AddSampleServiceClient(
        this IServiceCollection services,
        Action<SampleServiceClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new SampleServiceClientOptions();
        configure(options);

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            throw new InvalidOperationException("SampleServiceClientOptions.BaseUrl é obrigatória.");
        }

        services.AddHttpClient<ISampleServiceClient, SampleServiceClient>(client =>
            client.BaseAddress = new Uri(options.BaseUrl));

        return services;
    }
}
