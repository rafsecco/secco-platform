namespace Secco.SecureGate.Application;

/// <summary>
/// Scopes OAuth da plataforma servidos pelo SecureGate (decisão da Fase 6.3, ADR-0020/0021):
/// least privilege por construção — o client de um produto só lê o catálogo do próprio produto.
/// </summary>
public static class SecureGateScopes
{
    /// <summary>
    /// Scope administrativo do SecureGate (gestão de tenants e bancos). Concedido apenas
    /// ao AdminPortal e a operadores — nunca a clients de produto.
    /// </summary>
    public const string Admin = "securegate:admin";

    /// <summary>Prefixo dos scopes de leitura de catálogo por produto (<c>catalog:&lt;produto&gt;</c>).</summary>
    public const string CatalogPrefix = "catalog:";

    /// <summary>Monta o scope de leitura de catálogo de um produto (ex.: <c>catalog:logstream</c>).</summary>
    /// <param name="product">Identificador do produto (kebab-case minúsculo).</param>
    public static string CatalogFor(string product) => CatalogPrefix + product;
}
