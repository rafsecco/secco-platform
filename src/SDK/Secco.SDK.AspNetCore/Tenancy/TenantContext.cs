namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Implementação mutável de <see cref="ITenantContext"/>. Pública apenas porque
/// <see cref="SeccoTenancyMiddleware.InvokeAsync"/> precisa recebê-la via DI (o
/// ASP.NET Core exige assinatura pública em métodos de middleware); o setter é interno
/// para que só o próprio middleware (mesmo assembly) possa definir o valor.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    /// <inheritdoc />
    public Guid? TenantId { get; internal set; }

    /// <inheritdoc />
    public bool IsResolved => TenantId is not null;
}
