namespace Secco.NotificationHub.Application;

/// <summary>
/// Canais de entrega reconhecidos (Fase 8.4). Diferente de <c>Source</c>/<c>Type</c>
/// (texto livre — o Hub nunca interpreta), um canal mapeia direto para um caminho de
/// código que precisa existir aqui dentro; por isso é um conjunto fechado, validado.
/// </summary>
public static class NotificationHubChannels
{
    /// <summary>Envio por e-mail (assíncrono, com retry — ADR-0015 Camada 2).</summary>
    public const string Email = "email";

    /// <summary>Item no inbox in-app do usuário.</summary>
    public const string InApp = "in_app";

    /// <summary>Todos os canais reconhecidos, para validação de entrada.</summary>
    public static readonly IReadOnlyCollection<string> All = [Email, InApp];
}
