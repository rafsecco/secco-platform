using Microsoft.Extensions.DependencyInjection;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.NotificationHub.Application.Notifications;

namespace Secco.NotificationHub.Application;

/// <summary>Composição de DI da camada de aplicação.</summary>
public static class NotificationHubApplicationExtensions
{
    /// <summary>
    /// Registra os casos de uso. As options são registradas pela Infrastructure
    /// (bind lazy da configuração) — a Application não conhece configuração.
    /// </summary>
    /// <param name="services">Coleção de serviços da aplicação.</param>
    public static IServiceCollection AddNotificationHubApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<DispatchNotificationHandler>();
        services.AddScoped<GetNotificationByIdHandler>();

        services.AddScoped<GetUnreadInAppNotificationsHandler>();
        services.AddScoped<CountUnreadInAppNotificationsHandler>();
        services.AddScoped<MarkInAppNotificationAsReadHandler>();

        return services;
    }
}
