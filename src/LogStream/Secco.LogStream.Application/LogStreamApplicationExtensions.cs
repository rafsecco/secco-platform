using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Application.ApiCalls;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Application.LogProcesses;

namespace Secco.LogStream.Application;

/// <summary>Composição de DI da camada de aplicação do LogStream.</summary>
public static class LogStreamApplicationExtensions
{
    /// <summary>
    /// Registra os casos de uso. As options (limites de ingestão etc.) são registradas
    /// pela Infrastructure, que faz o bind lazy da configuração — a Application não
    /// conhece configuração.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddLogStreamApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<CreateLogEntryHandler>();
        services.AddScoped<CreateLogEntryBatchHandler>();
        services.AddScoped<GetLogEntryByIdHandler>();
        services.AddScoped<SearchLogEntriesHandler>();

        services.AddScoped<CreateLogProcessHandler>();
        services.AddScoped<CreateLogProcessDetailHandler>();
        services.AddScoped<GetLogProcessByIdHandler>();
        services.AddScoped<SearchLogProcessesHandler>();
        services.AddScoped<GetLogProcessDetailsHandler>();

        services.AddScoped<CreateApiCallLogHandler>();
        services.AddScoped<GetApiCallLogByIdHandler>();
        services.AddScoped<SearchApiCallLogsHandler>();

        return services;
    }
}
