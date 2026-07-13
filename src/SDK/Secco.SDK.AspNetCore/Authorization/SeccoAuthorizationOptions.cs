namespace Secco.SDK.AspNetCore.Authorization;

/// <summary>Opções da autorização granular (ADR-0021), seção <c>Secco:Authorization</c>.</summary>
public sealed class SeccoAuthorizationOptions
{
    /// <summary>Chave da seção de configuração.</summary>
    public const string SectionKey = "Secco:Authorization";

    /// <summary>
    /// TTL do cache de permissões por <c>(tenant_id, role)</c>, em segundos.
    /// Padrão 60; a ADR-0021 recomenda 60–300 — quanto menor, mais rápido uma
    /// revogação propaga (é o teto da janela de revogação).
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 60;
}
