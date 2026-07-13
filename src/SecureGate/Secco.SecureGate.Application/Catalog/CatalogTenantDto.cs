using Secco.SecureGate.Domain.Tenants;

namespace Secco.SecureGate.Application.Catalog;

/// <summary>
/// Entrada do catálogo servida aos produtos (ADR-0005): o par que o <c>ITenantCatalog</c>
/// do SDK consome. Contém a connection string — só sai por endpoint autorizado com o
/// scope <c>catalog:&lt;produto&gt;</c> correspondente (ADR-0020/0021).
/// </summary>
/// <param name="TenantId">Identificador do tenant.</param>
/// <param name="ConnectionString">Connection string do banco dedicado do tenant no produto. Nunca logar.</param>
public sealed record CatalogTenantDto(Guid TenantId, string ConnectionString)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    /// <param name="database">Entidade de origem.</param>
    public static CatalogTenantDto FromEntity(TenantDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        return new CatalogTenantDto(database.TenantId, database.ConnectionString);
    }
}
