namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>
/// Configuração da autenticação da plataforma (ADR-0007), seção <c>Secco:Authentication</c>.
/// Exatamente <b>um</b> dos modos deve estar configurado: <see cref="Authority"/> (OIDC/JWKS —
/// o modo de produção, SecureGate na Fase 6) ou <see cref="DevelopmentSigningKey"/> (HS256
/// local, proibido em Production). As regras são validadas no startup — fail-fast (ADR-0020).
/// </summary>
public sealed class SeccoAuthenticationOptions
{
    /// <summary>Seção de configuração de onde estas opções são lidas.</summary>
    public const string SectionKey = "Secco:Authentication";

    /// <summary>Tamanho mínimo da chave simétrica de desenvolvimento (HS256).</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>Authority OIDC (validação por JWKS via discovery). Modo de produção.</summary>
    public string? Authority { get; set; }

    /// <summary>Audience esperada nos tokens desta API (ex.: <c>secco-logstream</c>). Obrigatória.</summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Issuer esperado. Obrigatório no modo <see cref="DevelopmentSigningKey"/>;
    /// opcional no modo <see cref="Authority"/> (o discovery informa o issuer).
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>Exige HTTPS no endpoint de discovery. Sempre forçado em Production.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Chave simétrica (HS256) para tokens locais de desenvolvimento/staging enquanto o
    /// SecureGate não existe. <b>Proibida em Production</b> — o startup falha.
    /// </summary>
    public string? DevelopmentSigningKey { get; set; }
}
