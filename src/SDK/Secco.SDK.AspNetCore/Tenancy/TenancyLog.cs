using Microsoft.Extensions.Logging;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>Mensagens de log estruturadas da tenancy (source generator — ADR-0008: nunca interpolação).</summary>
internal static partial class TenancyLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Requisição rejeitada: o header X-Tenant-Id diverge da claim tenant_id do token — possível tentativa de acesso cross-tenant.")]
    public static partial void TenantConflict(ILogger logger);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Catálogo de tenants indisponível — requisição respondida com 503 + Retry-After.")]
    public static partial void CatalogUnavailable(ILogger logger, Exception exception);
}
