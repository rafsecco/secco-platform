namespace Secco.SecureGate.Client.Catalog;

/// <summary>
/// Opções do catálogo remoto de tenants (seção <c>Secco:SecureGate</c>). Presença de
/// QUALQUER chave da seção liga o modo remoto — e então todas passam a ser obrigatórias
/// (fail-fast, ADR-0020: configuração parcial nunca degrada silenciosamente para o
/// catálogo por configuração).
/// </summary>
public sealed class SecureGateTenantCatalogOptions
{
    /// <summary>Chave da seção de configuração.</summary>
    public const string SectionKey = "Secco:SecureGate";

    /// <summary>URL base da API do SecureGate (raiz, ex.: <c>https://securegate.interno</c>).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Client id (client credentials) do serviço consumidor.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret do serviço consumidor. Nunca logar.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Produto cujo catálogo será lido (ex.: <c>logstream</c>) — define o scope <c>catalog:&lt;produto&gt;</c> solicitado.</summary>
    public string? Product { get; set; }

    /// <summary>TTL do cache de entradas do catálogo, em segundos (padrão: 300).</summary>
    public int CacheTtlSeconds { get; set; } = 300;

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

    /// <summary>Valida o conjunto completo quando o modo remoto está ligado.</summary>
    /// <exception cref="InvalidOperationException">Se alguma chave obrigatória estiver ausente ou inválida.</exception>
    internal void Validate()
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

        if (string.IsNullOrWhiteSpace(Product))
        {
            missing.Add(nameof(Product));
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Catálogo remoto do SecureGate parcialmente configurado — faltam: {string.Join(", ", missing)} " +
                $"(seção '{SectionKey}'). Configure todas as chaves ou remova a seção para usar o catálogo por configuração.");
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
