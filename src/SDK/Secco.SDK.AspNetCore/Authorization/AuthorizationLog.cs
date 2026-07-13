using Microsoft.Extensions.Logging;

namespace Secco.SDK.AspNetCore.Authorization;

/// <summary>Mensagens de log estruturadas da autorização (source generator — ADR-0008).</summary>
internal static partial class AuthorizationLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Resolução de permissões indisponível para o role {Role} — acesso negado (fail-closed, ADR-0021).")]
    public static partial void PermissionResolutionFailed(ILogger logger, string role, Exception exception);
}
