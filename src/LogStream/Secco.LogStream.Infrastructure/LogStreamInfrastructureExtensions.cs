using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Application;
using Secco.LogStream.Application.ApiCalls;
using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Application.LogProcesses;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.LogStream.Infrastructure.Ingestion;
using Secco.LogStream.Infrastructure.Repositories;
using Secco.LogStream.Infrastructure.Retention;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.LogStream.Infrastructure;

/// <summary>Composição de DI da camada de infraestrutura do LogStream.</summary>
public static class LogStreamInfrastructureExtensions
{
    /// <summary>
    /// Registra o <see cref="LogStreamDbContext"/> apontando para o banco do tenant da
    /// requisição atual (ADR-0005): a connection string vem do <see cref="ITenantConnectionFactory"/>
    /// — jamais fixa. Requer <c>AddSeccoTenancy()</c> (via <c>AddSeccoPlatform()</c>).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddLogStreamInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Bind LAZY (do IConfiguration do DI): a configuração só é lida quando o host está
        // completo — fontes adicionadas por testes/hosting tardio são respeitadas
        services.AddSingleton(sp => BindSection(sp, "LogStream:Database", new LogStreamDatabaseOptions()));
        services.AddSingleton(sp => BindSection(sp, "LogStream:Retention", new LogStreamRetentionOptions()));
        services.AddSingleton(sp => BindSection(sp, "LogStream:Ingestion", new LogStreamIngestionOptions()));

        services.AddHostedService<LogRetentionWorker>();

        services.AddDbContext<LogStreamDbContext>((serviceProvider, options) =>
        {
            var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
            var databaseOptions = serviceProvider.GetRequiredService<LogStreamDatabaseOptions>();

            // O catálogo padrão resolve de forma síncrona (ValueTask já concluída);
            // catálogos remotos futuros devem manter cache para este caminho ser barato.
            var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

            LogStreamDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);
        });

        services.AddScoped<ILogEntryRepository, LogEntryRepository>();
        services.AddScoped<ILogProcessRepository, LogProcessRepository>();
        services.AddScoped<IApiCallLogRepository, ApiCallLogRepository>();

        // Ingestão assíncrona: canal bounded compartilhado + adaptador por request + worker
        services.AddSingleton<LogEntryIngestionChannel>();
        services.AddScoped<ILogIngestionQueue, LogEntryIngestionQueue>();
        services.AddHostedService<LogEntryIngestionWorker>();

        return services;
    }

    private static TOptions BindSection<TOptions>(IServiceProvider serviceProvider, string sectionKey, TOptions options)
        where TOptions : class
    {
        serviceProvider.GetRequiredService<IConfiguration>().GetSection(sectionKey).Bind(options);
        return options;
    }

    /// <summary>
    /// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo.
    /// Uso: startup em Development e processos controlados de provisionamento (ADR-0005) —
    /// nunca no startup de produção.
    /// </summary>
    /// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task MigrateLogStreamTenantDatabasesAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ITenantCatalog>();
        var databaseOptions = scope.ServiceProvider.GetRequiredService<LogStreamDatabaseOptions>();

        foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var options = LogStreamDatabaseProviderConfigurator.CreateOptions(
                databaseOptions.Provider, tenant.ConnectionString);

            await using var context = new LogStreamDbContext(options);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
