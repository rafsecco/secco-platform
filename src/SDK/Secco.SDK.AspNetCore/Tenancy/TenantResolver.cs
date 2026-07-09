namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>Resultado da resolução de tenant de uma requisição.</summary>
/// <param name="TenantId">Tenant resolvido; nulo se nenhuma fonte válida o determinou.</param>
/// <param name="IsConflict">Indica divergência entre claim e header — a requisição deve ser rejeitada.</param>
internal readonly record struct TenantResolution(Guid? TenantId, bool IsConflict)
{
    public static readonly TenantResolution Unresolved = new(null, false);

    public static readonly TenantResolution Conflict = new(null, true);

    public static TenantResolution Resolved(Guid tenantId) => new(tenantId, false);
}

/// <summary>
/// Regras de precedência da ADR-0005 com a postura de confiança da ADR-0020:
/// a claim <c>tenant_id</c> (assinada pelo SecureGate) é a fonte primária; o header
/// <c>X-Tenant-Id</c> só é considerado quando não há claim. Divergência entre os dois
/// é conflito (possível tentativa cross-tenant) e claim presente porém inválida não
/// cai para o header — um token corrompido não abre espaço para o chamador escolher tenant.
/// </summary>
internal static class TenantResolver
{
    public static TenantResolution Resolve(string? claimValue, string? headerValue)
    {
        var hasHeader = !string.IsNullOrEmpty(headerValue);

        if (!string.IsNullOrEmpty(claimValue))
        {
            if (!TryParseStrict(claimValue, out var claimTenant))
            {
                return TenantResolution.Unresolved;
            }

            if (hasHeader && (!TryParseStrict(headerValue, out var headerTenant) || headerTenant != claimTenant))
            {
                return TenantResolution.Conflict;
            }

            return TenantResolution.Resolved(claimTenant);
        }

        return hasHeader && TryParseStrict(headerValue, out var fromHeader)
            ? TenantResolution.Resolved(fromHeader)
            : TenantResolution.Unresolved;
    }

    private static bool TryParseStrict(string? value, out Guid tenantId) =>
        Guid.TryParse(value, out tenantId) && tenantId != Guid.Empty;
}
