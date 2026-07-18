namespace Secco.NotificationHub.Application;

/// <summary>
/// Limites de entrada do produto (ADR-0020), configuráveis na seção
/// <c>NotificationHub:Limits</c> — bind lazy feito pela Infrastructure.
/// </summary>
public sealed class NotificationHubOptions
{
    /// <summary>Tamanho máximo do destinatário (default 254 — teto prático de um e-mail, RFC 5321).</summary>
    public int MaxRecipientLength { get; set; } = 254;

    /// <summary>Tamanho máximo do assunto (default 998 — teto prático de uma linha de header, RFC 5322).</summary>
    public int MaxSubjectLength { get; set; } = 998;

    /// <summary>Tamanho máximo do corpo (default 64 KB).</summary>
    public int MaxBodyLength { get; set; } = 65_536;
}
