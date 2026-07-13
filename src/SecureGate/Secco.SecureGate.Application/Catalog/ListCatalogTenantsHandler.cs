using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Catalog;

/// <summary>
/// Lista o catálogo completo de um produto (tenants ATIVOS com banco cadastrado) —
/// alimenta o <c>ListAsync</c> do <c>ITenantCatalog</c> remoto, usado por processos
/// que iteram os bancos de tenant (migrations, retenção), nunca em fluxo de requisição.
/// </summary>
public sealed class ListCatalogTenantsHandler(ITenantRepository repository)
{
    /// <summary>Executa o caso de uso.</summary>
    /// <param name="product">Produto informado na rota.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task<Result<IReadOnlyList<CatalogTenantDto>>> HandleAsync(
        string? product,
        CancellationToken cancellationToken = default)
    {
        var normalized = product?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!TenantInputRules.IsValidSlug(normalized, TenantDatabase.ProductMaxLength))
        {
            return SecureGateErrors.Catalog.ProductInvalid;
        }

        var databases = await repository.ListActiveDatabasesAsync(normalized, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<CatalogTenantDto>>(
            [.. databases.Select(CatalogTenantDto.FromEntity)]);
    }
}
