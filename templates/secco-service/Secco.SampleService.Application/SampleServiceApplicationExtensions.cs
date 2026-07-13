using Microsoft.Extensions.DependencyInjection;
using Secco.SampleService.Application.Samples;

namespace Secco.SampleService.Application;

/// <summary>Composição de DI da camada de aplicação.</summary>
public static class SampleServiceApplicationExtensions
{
    /// <summary>
    /// Registra os casos de uso. As options são registradas pela Infrastructure
    /// (bind lazy da configuração) — a Application não conhece configuração.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddSampleServiceApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<CreateSampleHandler>();
        services.AddScoped<GetSampleByIdHandler>();
        services.AddScoped<SearchSamplesHandler>();

        return services;
    }
}
