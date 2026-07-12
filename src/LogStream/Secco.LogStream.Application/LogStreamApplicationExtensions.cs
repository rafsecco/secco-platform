using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Application.ApiCalls;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Application.LogProcesses;

namespace Secco.LogStream.Application;

/// <summary>Composição de DI da camada de aplicação do LogStream.</summary>
public static class LogStreamApplicationExtensions
{
    /// <summary>
    /// Registra os casos de uso e os limites de ingestão. A borda (Api) faz o bind da
    /// seção <c>LogStream:Ingestion</c> pelo delegate — a Application não conhece configuração.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="configureIngestion">Ajuste dos limites de ingestão sobre os defaults.</param>
    public static IServiceCollection AddLogStreamApplication(
        this IServiceCollection services,
        Action<LogStreamIngestionOptions>? configureIngestion = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new LogStreamIngestionOptions();
        configureIngestion?.Invoke(options);
        services.AddSingleton(options);

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
