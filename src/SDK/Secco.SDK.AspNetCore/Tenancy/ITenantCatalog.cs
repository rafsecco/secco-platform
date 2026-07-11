namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>
/// Catálogo central de tenants (ADR-0005). A implementação padrão lê de
/// <c>IConfiguration</c> (<see cref="ConfigurationTenantCatalog"/>); o catálogo SQL
/// gerenciado pelo AdminPortal (Fase 7) entrará como outra implementação desta interface.
/// Produtos podem substituir o registro no DI antes de chamar <c>AddSeccoTenancy()</c>.
/// </summary>
public interface ITenantCatalog
{
    /// <summary>Busca um tenant no catálogo.</summary>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O registro do tenant, ou nulo se não cadastrado.</returns>
    ValueTask<TenantInfo?> FindAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista todos os tenants do catálogo — usado por processos que iteram os bancos de
    /// tenant (migrations em DEV, retenção, provisionamento). Nunca em fluxo de requisição.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    ValueTask<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default);
}
