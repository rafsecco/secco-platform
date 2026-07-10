using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Secco.SDK.AspNetCore.Resilience;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de DI para resiliência HTTP padrão da plataforma (ADR-0004).</summary>
public static class SeccoResilienceServiceCollectionExtensions
{
    /// <summary>
    /// Aplica o pipeline de resiliência padrão (rate limiter → timeout total → retry
    /// exponencial com jitter → circuit breaker → timeout por tentativa) a <b>todo</b>
    /// <c>HttpClient</c> registrado via <c>AddHttpClient</c> — incluindo os clients NSwag
    /// dos produtos. Retry automático só para métodos idempotentes (RFC 9110): repetir
    /// POST/PATCH após timeout pode duplicar efeitos; eles seguem protegidos por timeout
    /// e circuit breaker.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configure">
    /// Ajuste fino opcional das opções do pipeline — executa depois dos padrões da
    /// plataforma e pode sobrescrevê-los (inclusive o predicado de retry, se um produto
    /// assumir conscientemente retry de POST).
    /// </param>
    public static IServiceCollection AddSeccoResilience(
        this IServiceCollection services,
        Action<HttpStandardResilienceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Guarda de chamada dupla: registrar o handler duas vezes empilharia dois
        // pipelines de resiliência (retry 3x aninhado = até 16 tentativas).
        if (services.Any(descriptor => descriptor.ServiceType == typeof(SeccoResilienceMarker)))
        {
            return services;
        }

        services.AddSingleton<SeccoResilienceMarker>();

        services.ConfigureHttpClientDefaults(http =>
            http.AddStandardResilienceHandler(options =>
            {
                options.Retry.ShouldHandle = args =>
                {
                    // Sem request no contexto (não deveria ocorrer no pipeline HTTP),
                    // a postura é conservadora: não repetir.
                    var request = args.Context.GetRequestMessage();
                    var eligible = request is not null
                        && IdempotentHttpMethods.Contains(request.Method)
                        && HttpClientResiliencePredicates.IsTransient(args.Outcome);

                    return ValueTask.FromResult(eligible);
                };

                configure?.Invoke(options);
            }));

        return services;
    }

    /// <summary>Marcador de registro único do pipeline de resiliência.</summary>
    internal sealed class SeccoResilienceMarker;
}
