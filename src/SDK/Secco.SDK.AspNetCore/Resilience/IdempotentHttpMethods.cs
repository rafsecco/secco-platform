namespace Secco.SDK.AspNetCore.Resilience;

/// <summary>
/// Métodos HTTP idempotentes (RFC 9110 §9.2.2) — os únicos elegíveis a retry automático:
/// repetir POST/PATCH após um timeout pode duplicar o efeito no servidor. Quando a
/// plataforma tiver idempotency keys (ADR futura do backlog), essa regra será revista.
/// </summary>
internal static class IdempotentHttpMethods
{
    public static bool Contains(HttpMethod method) =>
        method == HttpMethod.Get
        || method == HttpMethod.Head
        || method == HttpMethod.Put
        || method == HttpMethod.Delete
        || method == HttpMethod.Options
        || method == HttpMethod.Trace;
}
