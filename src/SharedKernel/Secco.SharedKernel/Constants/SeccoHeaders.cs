namespace Secco.SharedKernel.Constants;

/// <summary>
/// Headers HTTP padronizados da plataforma, propagados pelo Secco.SDK em toda a cadeia.
/// </summary>
public static class SeccoHeaders
{
    /// <summary>Correlação de requisições ponta a ponta (ADR-0004/0008).</summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>Resolução de tenant em cenários internos — a claim do token é o mecanismo primário (ADR-0005).</summary>
    public const string TenantId = "X-Tenant-Id";
}
