namespace Secco.SecureGate.Client.Catalog;

/// <summary>
/// Opções de conexão dos serviços da plataforma com o SecureGate (seção <c>Secco:SecureGate</c>) —
/// compartilhadas pelo catálogo de tenants (Fase 6.3) e pela resolução de permissões
/// (Fase 6.4). Presença de QUALQUER chave da seção liga o modo remoto — e então as chaves
/// exigidas pelo recurso passam a ser obrigatórias (fail-fast, ADR-0020: configuração
/// parcial nunca degrada silenciosamente para as implementações por configuração).
/// </summary>
public sealed class SecureGateClientCredentialsOptions
{
    /// <summary>Chave da seção de configuração.</summary>
    public const string SectionKey = "Secco:SecureGate";

    /// <summary>URL base da API do SecureGate (raiz, ex.: <c>https://securegate.interno</c>).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Client id (client credentials) do serviço consumidor.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret do serviço consumidor. Nunca logar.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Produto cujo catálogo será lido (ex.: <c>logstream</c>) — define o scope <c>catalog:&lt;produto&gt;</c>. Exigido só pelo catálogo.</summary>
    public string? Product { get; set; }

    /// <summary>TTL do cache de entradas do catálogo de tenants, em segundos (padrão: 300).</summary>
    public int CacheTtlSeconds { get; set; } = 300;

    /// <summary>Scope de resolução de permissões (ADR-0021, Fase 6.4).</summary>
    internal const string AuthorizationScope = "authorization:read";

    /// <summary>Indica se alguma chave da seção foi configurada (modo remoto ligado).</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        || !string.IsNullOrWhiteSpace(ClientId)
        || !string.IsNullOrWhiteSpace(ClientSecret)
        || !string.IsNullOrWhiteSpace(Product);

    /// <summary>Scope de catálogo solicitado ao SecureGate (least privilege, ADR-0020/0021).</summary>
    internal string CatalogScope => "catalog:" + NormalizedProduct;

    /// <summary>Produto normalizado (minúsculo, sem espaços).</summary>
    internal string NormalizedProduct => Product?.Trim().ToLowerInvariant() ?? string.Empty;

    /// <summary>Valida o conjunto exigido quando o modo remoto está ligado.</summary>
    /// <param name="requireProduct"><c>true</c> para o catálogo (o produto define o scope); a resolução de permissões não o exige.</param>
    /// <exception cref="InvalidOperationException">Se alguma chave obrigatória estiver ausente ou inválida.</exception>
    internal void Validate(bool requireProduct)
    {
        var missing = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            missing.Add(nameof(BaseUrl));
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            missing.Add(nameof(ClientId));
        }

        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            missing.Add(nameof(ClientSecret));
        }

        if (requireProduct && string.IsNullOrWhiteSpace(Product))
        {
            missing.Add(nameof(Product));
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Conexão com o SecureGate parcialmente configurada — faltam: {string.Join(", ", missing)} " +
                $"(seção '{SectionKey}'). Configure todas as chaves ou remova a seção para usar as implementações por configuração.");
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"'{SectionKey}:BaseUrl' deve ser uma URL http(s) absoluta.");
        }

        if (CacheTtlSeconds <= 0)
        {
            throw new InvalidOperationException(
                $"'{SectionKey}:CacheTtlSeconds' deve ser maior que zero.");
        }
    }
}
