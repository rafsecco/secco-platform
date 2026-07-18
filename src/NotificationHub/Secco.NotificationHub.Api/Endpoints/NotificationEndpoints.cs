using Secco.NotificationHub.Application;
using Secco.NotificationHub.Application.Notifications;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.NotificationHub.Api.Endpoints;

/// <summary>Payload de despacho de uma notificação para 1+ canais (Fase 8.4).</summary>
/// <param name="UserId">Dono do item no inbox in-app. Obrigatório se <paramref name="Channels"/> incluir <c>in_app</c>.</param>
/// <param name="Recipient">E-mail do destinatário, já resolvido pelo chamador. Obrigatório se <paramref name="Channels"/> incluir <c>email</c>.</param>
/// <param name="Title">Título pronto (vira o assunto do e-mail, quando solicitado). Obrigatório.</param>
/// <param name="Message">Mensagem pronta (vira o corpo do e-mail, quando solicitado). Obrigatório.</param>
/// <param name="Source">Origem, texto livre — o Hub nunca interpreta. Opcional.</param>
/// <param name="Type">Tipo, texto livre — o Hub nunca interpreta. Opcional.</param>
/// <param name="Link">Link de destino do item in-app, quando houver. Opcional.</param>
/// <param name="Channels">Canais de entrega: <c>email</c>, <c>in_app</c>, ou ambos.</param>
public sealed record DispatchNotificationRequest(
    Guid? UserId,
    string? Recipient,
    string? Title,
    string? Message,
    string? Source,
    string? Type,
    string? Link,
    IReadOnlyCollection<string>? Channels);

/// <summary>Endpoints de notificações (<c>/api/v1/notifications</c>, ADR-0010).</summary>
public static class NotificationEndpoints
{
    /// <summary>Mapeia os endpoints de notificações.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/notifications").WithTags("Notifications");

        group.MapPost("/", async (
                DispatchNotificationRequest request, DispatchNotificationHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(
                new DispatchNotificationCommand(
                    request.UserId, request.Recipient, request.Title, request.Message,
                    request.Source, request.Type, request.Link, request.Channels),
                cancellationToken))
                .ToHttpResult(result => Results.Accepted(value: result)))
            .RequireAuthorization(NotificationHubPermissions.Notifications.Write)
            .WithSummary("Despacha uma notificação para 1+ canais (e-mail assíncrono com retry — ADR-0015; in-app gravado de imediato).")
            .Produces<DispatchNotificationResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, GetNotificationByIdHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken))
                .ToHttpResult(dto => Results.Ok(dto)))
            .RequireAuthorization(NotificationHubPermissions.Notifications.Read)
            .WithSummary("Consulta o status de uma notificação por e-mail (Pending/Sent/Failed).")
            .Produces<NotificationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
