using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.NotificationHub.Domain.Notifications;

/// <summary>
/// Uma notificação por e-mail (Fase 8, v1): o chamador já resolveu o destinatário e
/// montou o conteúdo pronto — este produto só enfileira, envia e rastreia o status
/// (BaseEntity Guid v7, ADR-0017).
/// </summary>
public sealed class Notification : BaseEntity
{
    private Notification()
    {
        // Construtor de rehidratação do EF Core
        Recipient = string.Empty;
        Subject = string.Empty;
        Body = string.Empty;
    }

    /// <summary>Cria uma notificação pendente de envio.</summary>
    /// <param name="recipient">E-mail do destinatário, já resolvido pelo chamador. Obrigatório.</param>
    /// <param name="subject">Assunto pronto. Obrigatório.</param>
    /// <param name="body">Corpo pronto (texto ou HTML). Obrigatório.</param>
    /// <exception cref="DomainInvariantException">Se destinatário, assunto ou corpo forem nulos/vazios.</exception>
    public Notification(string recipient, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            throw new DomainInvariantException("Uma notificação exige destinatário não vazio.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new DomainInvariantException("Uma notificação exige assunto não vazio.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            throw new DomainInvariantException("Uma notificação exige corpo não vazio.");
        }

        Recipient = recipient;
        Subject = subject;
        Body = body;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>E-mail do destinatário (coluna <c>ds_recipient</c>).</summary>
    public string Recipient { get; private set; }

    /// <summary>Assunto (coluna <c>ds_subject</c>).</summary>
    public string Subject { get; private set; }

    /// <summary>Corpo (coluna <c>ds_body</c>).</summary>
    public string Body { get; private set; }

    /// <summary>Estado do envio (coluna <c>ie_status</c>).</summary>
    public NotificationStatus Status { get; private set; }

    /// <summary>Motivo da falha, quando <see cref="Status"/> é <see cref="NotificationStatus.Failed"/> (coluna <c>ds_failure_reason</c>).</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Momento do envio bem-sucedido, quando houver (coluna <c>dt_sent_at</c>).</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Marca a notificação como enviada com sucesso.</summary>
    public void MarkAsSent()
    {
        Status = NotificationStatus.Sent;
        SentAt = DateTimeOffset.UtcNow;
        FailureReason = null;
    }

    /// <summary>Marca a notificação como falha definitiva (todas as tentativas de retry esgotadas).</summary>
    /// <param name="reason">Motivo da falha — nunca a exceção crua (ADR-0020: sem stack trace/detalhe de infraestrutura).</param>
    public void MarkAsFailed(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainInvariantException("O motivo da falha não pode ser vazio.");
        }

        Status = NotificationStatus.Failed;
        FailureReason = reason;
    }
}
