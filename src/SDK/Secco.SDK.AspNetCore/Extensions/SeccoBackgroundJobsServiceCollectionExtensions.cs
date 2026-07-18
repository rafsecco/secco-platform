using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.AspNetCore.BackgroundJobs;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de DI para jobs persistentes com retry (ADR-0015 Camada 2).</summary>
public static class SeccoBackgroundJobsServiceCollectionExtensions
{
    /// <summary>
    /// Registra o Hangfire (núcleo gratuito, LGPL — ADR-0015) com storage SQL Server como
    /// <see cref="IBackgroundJobScheduler"/>. Os jobs vivem no banco de <b>plataforma</b>
    /// informado — nunca no banco por tenant; o tenant_id viaja no payload e é restaurado
    /// automaticamente na execução (ADR-0005).
    /// </summary>
    /// <remarks>
    /// Não faz parte de <c>AddSeccoPlatform()</c>: produtos que não precisam de jobs
    /// persistentes não carregam Hangfire — "produtos isolados permanecem leves" (ADR-0015).
    /// </remarks>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    /// <param name="platformConnectionStringFactory">
    /// Resolve a connection string do banco de plataforma do produto (não por tenant) a
    /// partir do <see cref="IServiceProvider"/> final — LAZY: roda quando o Hangfire
    /// inicializa de fato (após o host montar toda a configuração, inclusive a de testes),
    /// nunca no registro de serviços em si.
    /// </param>
    public static IServiceCollection AddSeccoBackgroundJobs(
        this IServiceCollection services,
        Func<IServiceProvider, string> platformConnectionStringFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(platformConnectionStringFactory);

        services.AddHangfire((serviceProvider, configuration) =>
        {
            var connectionString = platformConnectionStringFactory(serviceProvider);
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            configuration
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings()
                .UseSqlServerStorage(connectionString);
        });

        services.AddHangfireServer();

        services.AddScoped(typeof(TenantJobRunner<,>));
        services.AddScoped<IBackgroundJobScheduler, HangfireBackgroundJobScheduler>();

        return services;
    }
}
