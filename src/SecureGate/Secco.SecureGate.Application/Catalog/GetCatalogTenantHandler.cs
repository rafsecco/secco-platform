using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Catalog;

/// <summary>
/// Resolve a entrada de catálogo de um tenant em um produto — o caminho quente do
/// <c>FindAsync</c> do <c>ITenantCatalog</c> remoto. Tenant desconhecido, desativado
/// ou sem banco no produto respondem o MESMO NotFound (ADR-0020).
/// </summary>
public sealed class GetCatalogTenantHandler(ITenantRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="product">Produto informado na rota.</param>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<CatalogTenantDto>> HandleAsync(
        string? product,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var normalized = product?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!TenantInputRules.IsValidSlug(normalized, TenantDatabase.ProductMaxLength))
        {
            return SecureGateErrors.Catalog.ProductInvalid;
        }

        var database = await repository.FindActiveDatabaseAsync(tenantId, normalized, cancellationToken)
            .ConfigureAwait(false);

        return database is null
            ? SecureGateErrors.Catalog.EntryNotFound
            : CatalogTenantDto.FromEntity(database);
    }
}
