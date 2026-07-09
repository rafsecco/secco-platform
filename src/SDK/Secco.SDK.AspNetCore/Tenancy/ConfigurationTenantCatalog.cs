using Microsoft.Extensions.Configuration;

namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Implementação padrão do <see cref="ITenantCatalog"/> sobre <c>IConfiguration</c>:
/// funciona com appsettings, variáveis de ambiente, Key Vault etc. Formato esperado:
/// <code>
/// "Secco": { "Tenancy": { "Tenants": {
///     "&lt;guid-do-tenant&gt;": { "ConnectionString": "..." }
/// } } }
/// </code>
/// </summary>
internal sealed class ConfigurationTenantCatalog(IConfiguration configuration) : ITenantCatalog
{
    /// <summary>Chave da seção de configuração onde os tenants são declarados.</summary>
    internal const string TenantsSectionKey = "Secco:Tenancy:Tenants";

    public ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var connectionString = configuration[$"{TenantsSectionKey}:{tenantId}:ConnectionString"];

        return ValueTask.FromResult(string.IsNullOrWhiteSpace(connectionString)
            ? null
            : new TenantInfo(tenantId, connectionString));
    }
}
