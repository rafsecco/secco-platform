using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.NotificationHub.Domain.InAppNotifications;

/// <summary>
/// Um item no inbox in-app de um usuário (Fase 8.4): o chamador decide o conteúdo e a
/// quem pertence — este produto só armazena e controla o estado de leitura. Ciclo de
/// vida próprio, diferente do e-mail (<see cref="Notifications.Notification"/>): não há
/// "falha de entrega", só lido/não lido (BaseEntity Guid v7, ADR-0017).
/// </summary>
public sealed class InAppNotification : BaseEntity
{
    private InAppNotification()
    {
        // Construtor de rehidratação do EF Core
        Source = string.Empty;
        Type = string.Empty;
        Title = string.Empty;
        Message = string.Empty;
    }

    /// <summary>Cria um item de inbox não lido.</summary>
    /// <param name="userId">Dono do item, já resolvido pelo chamador (opaco — o Hub não conhece SecureGate). Obrigatório.</param>
    /// <param name="source">Produto/origem de quem disparou, texto livre. Opcional.</param>
    /// <param name="type">Categoria dentro da origem, texto livre — o Hub nunca interpreta. Opcional.</param>
    /// <param name="title">Título pronto. Obrigatório.</param>
    /// <param name="message">Mensagem pronta. Obrigatório.</param>
    /// <param name="link">Link de destino ao clicar, quando houver. Opcional.</param>
    /// <exception cref="DomainInvariantException">Se o dono for <see cref="Guid.Empty"/>, ou título/mensagem forem nulos/vazios.</exception>
    public InAppNotification(Guid userId, string? source, string? type, string title, string message, string? link)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainInvariantException("Uma notificação in-app exige um dono (userId) não vazio.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainInvariantException("Uma notificação in-app exige título não vazio.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new DomainInvariantException("Uma notificação in-app exige mensagem não vazia.");
        }

        UserId = userId;
        Source = source ?? string.Empty;
        Type = type ?? string.Empty;
        Title = title;
        Message = message;
        Link = link;
        IsRead = false;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Dono do item no inbox — identificador opaco fornecido pelo chamador (coluna <c>user_id</c>, sem prefixo — Guid não-chave, ADR-0017).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Origem, texto livre (coluna <c>ds_source</c>).</summary>
    public string Source { get; private set; }

    /// <summary>Tipo, texto livre (coluna <c>ds_type</c>).</summary>
    public string Type { get; private set; }

    /// <summary>Título (coluna <c>ds_title</c>).</summary>
    public string Title { get; private set; }

    /// <summary>Mensagem (coluna <c>ds_message</c>).</summary>
    public string Message { get; private set; }

    /// <summary>Link de destino, quando houver (coluna <c>ds_link</c>).</summary>
    public string? Link { get; private set; }

    /// <summary>Estado de leitura (coluna <c>fl_read</c>).</summary>
    public bool IsRead { get; private set; }

    /// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Momento em que foi marcada como lida, quando houver (coluna <c>dt_read_at</c>).</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>Marca o item como lido.</summary>
    public void MarkAsRead()
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAt = DateTimeOffset.UtcNow;
    }
}
