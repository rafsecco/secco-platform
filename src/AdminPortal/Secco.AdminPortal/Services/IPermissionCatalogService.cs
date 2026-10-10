using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>Leitura do catálogo de permissões publicado pelos produtos, on-behalf-of o operador (ADR-0039).</summary>
public interface IPermissionCatalogService
{
	/// <summary>Lista o catálogo de todos os produtos que publicaram.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<ProductPermissions>> ListAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class SecureGatePermissionCatalogService(ISecureGateClientFactory clientFactory) : IPermissionCatalogService
{
	public async Task<IReadOnlyList<ProductPermissions>> ListAsync(CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var catalog = await client.ListPermissionCatalogAsync(cancellationToken).ConfigureAwait(false);

		return
		[
			.. catalog.Select(product => new ProductPermissions(
				product.Product,
				[.. product.Permissions.Select(p => new PermissionEntry(p.Name, p.Description, [.. p.AlsoDeclaredBy]))],
				product.PublishedAt)),
		];
	}
}
