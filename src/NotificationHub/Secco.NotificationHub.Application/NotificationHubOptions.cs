namespace Secco.NotificationHub.Application;

/// <summary>
/// Limites de entrada do produto (ADR-0020), configuráveis na seção
/// <c>NotificationHub:Limits</c> — bind lazy feito pela Infrastructure.
/// </summary>
public sealed class NotificationHubOptions
{
    /// <summary>Tamanho máximo do nome de um sample (default 256).</summary>
    public int MaxNameLength { get; set; } = 256;

    /// <summary>Tamanho máximo da descrição (default 4096).</summary>
    public int MaxDescriptionLength { get; set; } = 4_096;
}
