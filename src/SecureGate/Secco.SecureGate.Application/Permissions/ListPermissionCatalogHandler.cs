using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Permissions;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Permissions;

/// <summary>Lista o catálogo de permissões de todos os produtos (ADR-0039).</summary>
public sealed class ListPermissionCatalogHandler(IPermissionCatalogRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<ProductPermissionCatalogDto>>> HandleAsync(CancellationToken cancellationToken = default) =>
		Result.Success(await repository.ListAsync(cancellationToken).ConfigureAwait(false));
}

/// <summary>Lê o catálogo de permissões de um produto (ADR-0039).</summary>
public sealed class GetPermissionCatalogHandler(IPermissionCatalogRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="product">Produto da rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ProductPermissionCatalogDto>> HandleAsync(string? product, CancellationToken cancellationToken = default)
	{
		// Formato inválido nunca encontra nada: mesma resposta de produto que não publicou
		if (!TenantInputRules.IsValidSlug(product ?? string.Empty, PermissionCatalogEntry.ProductMaxLength))
		{
			return SecureGateErrors.PermissionCatalog.NotFound;
		}

		var catalog = await repository.GetAsync(product!, cancellationToken).ConfigureAwait(false);

		return catalog is null ? SecureGateErrors.PermissionCatalog.NotFound : Result.Success(catalog);
	}
}
