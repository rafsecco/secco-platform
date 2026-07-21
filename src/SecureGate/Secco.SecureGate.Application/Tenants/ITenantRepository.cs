using Secco.SecureGate.Domain.Tenants;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>
/// Persistência do catálogo de tenants (banco de PLATAFORMA, ADR-0022).
/// As consultas "ativas" alimentam o endpoint de catálogo — tenant desativado
/// some do catálogo e os produtos param de resolvê-lo em no máximo um TTL de cache.
/// </summary>
public interface ITenantRepository
{
    /// <summary>Busca um tenant pelo identificador.</summary>
    /// <param name="id">Identificador do tenant.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<Tenant?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Verifica se já existe tenant com o slug informado.</summary>
    /// <param name="slug">Slug normalizado (minúsculo).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Lista todos os tenants, ordenados por nome.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adiciona um tenant ao contexto (efetivado no <see cref="SaveChangesAsync"/>).</summary>
    /// <param name="tenant">Tenant a adicionar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);

    /// <summary>Busca o banco de um tenant em um produto, sem filtro de ativação (gestão).</summary>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="product">Identificador do produto (minúsculo).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<TenantDatabase?> GetDatabaseAsync(Guid tenantId, string product, CancellationToken cancellationToken = default);

    /// <summary>Adiciona um banco de tenant ao contexto (efetivado no <see cref="SaveChangesAsync"/>).</summary>
    /// <param name="database">Banco a adicionar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddDatabaseAsync(TenantDatabase database, CancellationToken cancellationToken = default);

    /// <summary>Lista os produtos com banco cadastrado para um tenant (visão de gestão).</summary>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<string>> ListDatabaseProductsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Busca a federação de autenticação de um tenant (ADR-0026), se houver.</summary>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<TenantFederation?> GetFederationAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Adiciona uma federação de tenant ao contexto (efetivada no <see cref="SaveChangesAsync"/>).</summary>
    /// <param name="federation">Federação a adicionar.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddFederationAsync(TenantFederation federation, CancellationToken cancellationToken = default);

    /// <summary>Busca o banco de um tenant ATIVO em um produto (leitura de catálogo).</summary>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="product">Identificador do produto (minúsculo).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<TenantDatabase?> FindActiveDatabaseAsync(Guid tenantId, string product, CancellationToken cancellationToken = default);

    /// <summary>Lista os bancos de todos os tenants ATIVOS de um produto (leitura de catálogo).</summary>
    /// <param name="product">Identificador do produto (minúsculo).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<TenantDatabase>> ListActiveDatabasesAsync(string product, CancellationToken cancellationToken = default);

    /// <summary>Efetiva as alterações pendentes.</summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
