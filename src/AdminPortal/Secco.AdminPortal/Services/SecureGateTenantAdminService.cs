using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>Gestão de tenants via <c>Secco.SecureGate.Client</c> autenticado (on-behalf-of, ADR-0023).</summary>
internal sealed class SecureGateTenantAdminService(ISecureGateClientFactory clientFactory) : ITenantAdminService
{
	public async Task<IReadOnlyList<TenantSummary>> ListTenantsAsync(CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var tenants = await client.ListTenantsAsync(cancellationToken).ConfigureAwait(false);

		return
		[
			.. tenants.Select(tenant => new TenantSummary(
				tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt))
		];
	}

	public async Task<TenantDetail> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var tenant = await client.GetTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return new TenantDetail(
			tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt, [.. tenant.Products]);
	}

	public async Task UpsertDatabaseAsync(
		Guid tenantId, string product, string connectionString, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.UpsertTenantDatabaseAsync(
			tenantId, product, new UpsertTenantDatabaseRequest { ConnectionString = connectionString }, cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<IReadOnlyList<TenantAdminSummary>> ListAdminsAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var admins = await client.ListTenantAdminsAsync(tenantId, cancellationToken).ConfigureAwait(false);

		return
		[
			.. admins.Select(admin => new TenantAdminSummary(
				admin.UserId, admin.Email, admin.DisplayName, admin.OwnerTenantId, admin.GrantedBy, admin.CreatedAt))
		];
	}

	public async Task<IReadOnlyList<TenantSummary>> ListAdministeredByAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);
		var tenants = await client.ListTenantsAsync(cancellationToken).ConfigureAwait(false);
		var administered = new List<TenantSummary>();

		foreach (var tenant in tenants)
		{
			var admins = await client.ListTenantAdminsAsync(tenant.Id, cancellationToken).ConfigureAwait(false);

			if (admins.Any(admin => admin.UserId == userId))
			{
				administered.Add(new TenantSummary(tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt));
			}
		}

		return administered;
	}

	public async Task GrantAdminAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.GrantTenantAdminAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
	}

	public async Task RevokeAdminAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		var client = await clientFactory.CreateAsync(cancellationToken).ConfigureAwait(false);

		await client.RevokeTenantAdminAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
	}
}
