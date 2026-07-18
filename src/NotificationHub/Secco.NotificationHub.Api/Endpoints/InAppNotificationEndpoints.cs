using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.InAppNotifications;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.NotificationHub.Api.Endpoints;

/// <summary>
/// Endpoints do inbox in-app (<c>/api/v1/in-app-notifications</c>, Fase 8.4, ADR-0010).
/// O chamador (ex.: a intranet) informa o <c>userId</c> por requisição — o Hub não conhece
/// SecureGate nem tenta descobrir "quem está logado" a partir do próprio token de serviço.
/// </summary>
public static class InAppNotificationEndpoints
{
    /// <summary>Mapeia os endpoints do inbox in-app.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapInAppNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/in-app-notifications").WithTags("InAppNotifications");

        group.MapGet("/", async (Guid userId, GetUnreadInAppNotificationsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(userId, cancellationToken)))
            .RequireAuthorization(NotificationHubPermissions.InAppNotifications.Read)
            .WithSummary("Lista o inbox não lido de um usuário, mais recentes primeiro.")
            .Produces<IReadOnlyList<InAppNotificationDto>>(StatusCodes.Status200OK);

        group.MapGet("/count", async (Guid userId, CountUnreadInAppNotificationsHandler handler, CancellationToken cancellationToken) =>
            Results.Ok(await handler.HandleAsync(userId, cancellationToken)))
            .RequireAuthorization(NotificationHubPermissions.InAppNotifications.Read)
            .WithSummary("Conta o inbox não lido de um usuário — para o sino do header, sem a lista completa.")
            .Produces<int>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/read", async (Guid id, MarkInAppNotificationAsReadHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken))
                .ToHttpResult(Results.NoContent))
            .RequireAuthorization(NotificationHubPermissions.InAppNotifications.Write)
            .WithSummary("Marca um item do inbox como lido (idempotente).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
