using Microsoft.Extensions.Configuration;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Implementação padrão do <see cref="ITenantCatalog"/> sobre <c>IConfiguration</c>:
/// funciona com appsettings, variáveis de ambiente, Key Vault etc. Pública por design —
/// catálogos remotos (ex.: <c>Secco.SecureGate.Client</c>) a usam como fallback quando a
/// conexão com o catálogo central não está configurada no ambiente. Formato esperado:
/// <code>
/// "Secco": { "Tenancy": { "Tenants": {
///     "&lt;guid-do-tenant&gt;": { "ConnectionString": "..." }
/// } } }
/// </code>
/// </summary>
public sealed class ConfigurationTenantCatalog(IConfiguration configuration) : ITenantCatalog
{
    /// <summary>Chave da seção de configuração onde os tenants são declarados.</summary>
    internal const string TenantsSectionKey = "Secco:Tenancy:Tenants";

    /// <inheritdoc />
    public ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration[$"{TenantsSectionKey}:{tenantId}:ConnectionString"];

        return ValueTask.FromResult(string.IsNullOrWhiteSpace(connectionString)
            ? null
            : new TenantInfo(tenantId, connectionString));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        var tenants = new List<TenantInfo>();

        foreach (var section in configuration.GetSection(TenantsSectionKey).GetChildren())
        {
            var connectionString = section["ConnectionString"];

            // Chaves que não são Guid ou sem connection string são ignoradas silenciosamente —
            // configuração malformada não pode derrubar processos de manutenção.
            if (Guid.TryParse(section.Key, out var tenantId)
                && tenantId != Guid.Empty
                && !string.IsNullOrWhiteSpace(connectionString))
            {
                tenants.Add(new TenantInfo(tenantId, connectionString));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<TenantInfo>>(tenants);
    }
}
