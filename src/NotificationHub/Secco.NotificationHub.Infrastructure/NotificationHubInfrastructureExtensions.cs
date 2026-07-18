using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Application.Notifications;
using Secco.NotificationHub.Infrastructure.Contexts;
using Secco.NotificationHub.Infrastructure.Email;
using Secco.NotificationHub.Infrastructure.Repositories;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.NotificationHub.Infrastructure;

/// <summary>Composição de DI da camada de infraestrutura.</summary>
public static class NotificationHubInfrastructureExtensions
{
    /// <summary>
    /// Registra o <see cref="NotificationHubDbContext"/> apontando para o banco do tenant da
    /// requisição atual (ADR-0005): a connection string vem do <see cref="ITenantConnectionFactory"/>
    /// — jamais fixa. Requer <c>AddSeccoTenancy()</c> (via <c>AddSeccoPlatform()</c>).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddNotificationHubInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Bind LAZY (do IConfiguration do DI): fontes adicionadas por testes/hosting tardio são respeitadas
        services.AddSingleton(sp => BindSection(sp, "NotificationHub:Database", new NotificationHubDatabaseOptions()));
        services.AddSingleton(sp => BindSection(sp, "NotificationHub:Limits", new NotificationHubOptions()));
        services.AddSingleton(sp => BindSection(sp, "NotificationHub:Email", new NotificationHubEmailOptions()));

        services.AddDbContext<NotificationHubDbContext>((serviceProvider, options) =>
        {
            var connectionFactory = serviceProvider.GetRequiredService<ITenantConnectionFactory>();
            var databaseOptions = serviceProvider.GetRequiredService<NotificationHubDatabaseOptions>();

            // O catálogo padrão resolve de forma síncrona (ValueTask já concluída)
            var connectionString = connectionFactory.GetConnectionStringAsync().AsTask().GetAwaiter().GetResult();

            NotificationHubDatabaseProviderConfigurator.Configure(options, databaseOptions.Provider, connectionString);
        });

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IInAppNotificationRepository, InAppNotificationRepository>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<IEmailDispatchQueue, EmailDispatchScheduler>();
        services.AddScoped<SendEmailJob>();

        return services;
    }

    /// <summary>
    /// Registra o Hangfire (ADR-0015 Camada 2) com storage no banco de PLATAFORMA — nunca
    /// por tenant. Chamada separada de <see cref="AddNotificationHubInfrastructure"/> só por
    /// clareza de composição (o wiring é conceitualmente distinto: fila, não dados de tenant).
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddNotificationHubBackgroundJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSeccoBackgroundJobs(serviceProvider =>
            BindSection(serviceProvider, "NotificationHub:BackgroundJobs", new NotificationHubBackgroundJobOptions())
                .ConnectionString);

        return services;
    }

    /// <summary>
    /// Aplica as migrations pendentes no banco de <b>cada tenant</b> do catálogo.
    /// Uso: startup em Development e processos controlados de provisionamento (ADR-0005) —
    /// nunca no startup de produção.
    /// </summary>
    /// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public static async Task MigrateNotificationHubTenantDatabasesAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        using var scope = serviceProvider.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<ITenantCatalog>();
        var databaseOptions = scope.ServiceProvider.GetRequiredService<NotificationHubDatabaseOptions>();

        foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            var options = NotificationHubDatabaseProviderConfigurator.CreateOptions(
                databaseOptions.Provider, tenant.ConnectionString);

            await using var context = new NotificationHubDbContext(options);
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TOptions BindSection<TOptions>(IServiceProvider serviceProvider, string sectionKey, TOptions options)
        where TOptions : class
    {
        serviceProvider.GetRequiredService<IConfiguration>().GetSection(sectionKey).Bind(options);
        return options;
    }
}
