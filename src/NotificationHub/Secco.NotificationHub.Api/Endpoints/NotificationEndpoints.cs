using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.Notifications;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.NotificationHub.Api.Endpoints;

/// <summary>Payload de envio de uma notificação por e-mail.</summary>
/// <param name="Recipient">E-mail do destinatário, já resolvido pelo chamador. Obrigatório.</param>
/// <param name="Subject">Assunto pronto. Obrigatório.</param>
/// <param name="Body">Corpo pronto (texto ou HTML). Obrigatório.</param>
public sealed record SendNotificationRequest(string? Recipient, string? Subject, string? Body);

/// <summary>Endpoints de notificações (<c>/api/v1/notifications</c>, ADR-0010).</summary>
public static class NotificationEndpoints
{
    /// <summary>Mapeia os endpoints de notificações.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications").WithTags("Notifications");

        group.MapPost("/", async (SendNotificationRequest request, SendNotificationHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(new SendNotificationCommand(request.Recipient, request.Subject, request.Body), cancellationToken))
                .ToHttpResult(dto => Results.Created($"/api/v1/notifications/{dto.Id}", dto)))
            .RequireAuthorization(NotificationHubPermissions.Notifications.Write)
            .WithSummary("Enfileira o envio de uma notificação por e-mail (assíncrono, com retry — ADR-0015).")
            .Produces<NotificationDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, GetNotificationByIdHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken))
                .ToHttpResult(dto => Results.Ok(dto)))
            .RequireAuthorization(NotificationHubPermissions.Notifications.Read)
            .WithSummary("Consulta o status de uma notificação (Pending/Sent/Failed).")
            .Produces<NotificationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
