using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.OpenIddict;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>
/// Clients de produto sobre o OpenIddict (ADR-0037). Toda busca filtra por tenant E por origem Api;
/// a escrita passa pelo manager do OpenIddict, que hasheia o secret e leva junto tokens e
/// autorizações na remoção.
/// </summary>
internal sealed class OpenIddictProductClientStore(
	SecureGateDbContext context,
	OpenIddictApplicationManager<OidcApplication> applications) : IProductClientStore
{
	private IQueryable<OidcApplication> TenantClients(Guid tenantId) =>
		context.Set<OidcApplication>().Where(a => a.TenantId == tenantId && a.Origin == ClientOrigin.Api);

	public Task<bool> NameExistsAsync(Guid tenantId, string name, string? exceptClientId, CancellationToken cancellationToken = default) =>
		TenantClients(tenantId).AnyAsync(a => a.Name == name && a.ClientId != exceptClientId, cancellationToken);

	public async Task<ProductClientDto> CreateAsync(
		Guid tenantId, string clientId, string clientSecret, ProductClientAccess access, CancellationToken cancellationToken = default)
	{
		// Um INSERT só, com tenant e origem já preenchidos: nunca existe, nem por um instante, um
		// client com escopo de produto e sem tenant.
		var application = new OidcApplication
		{
			TenantId = tenantId,
			Origin = ClientOrigin.Api,
			Name = access.Name,
			Roles = JoinRoles(access.Roles),
		};

		await applications.PopulateAsync(application, Describe(clientId, access), cancellationToken).ConfigureAwait(false);
		await applications.CreateAsync(application, clientSecret, cancellationToken).ConfigureAwait(false);

		return ToDto(application);
	}

	public async Task<IReadOnlyList<ProductClientDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		[.. (await TenantClients(tenantId).AsNoTracking().OrderBy(a => a.Name).ToListAsync(cancellationToken).ConfigureAwait(false))
			.Select(ToDto)];

	public async Task<ProductClientDto?> GetAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		await FindAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is { } application ? ToDto(application) : null;

	public async Task<bool> UpdateAsync(
		Guid tenantId, string clientId, ProductClientAccess access, CancellationToken cancellationToken = default)
	{
		if (await FindAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is not { } application)
		{
			return false;
		}

		// Descriptor a partir do estado atual: o secret entra já hasheado, e o manager só re-hasheia
		// quando o valor muda — alterar acesso nunca troca a credencial.
		var descriptor = new OpenIddictApplicationDescriptor();
		await applications.PopulateAsync(descriptor, application, cancellationToken).ConfigureAwait(false);

		var desired = Describe(clientId, access);
		descriptor.DisplayName = desired.DisplayName;
		descriptor.Permissions.Clear();
		descriptor.Permissions.UnionWith(desired.Permissions);

		application.Name = access.Name;
		application.Roles = JoinRoles(access.Roles);

		await applications.UpdateAsync(application, descriptor, cancellationToken).ConfigureAwait(false);
		return true;
	}

	public async Task<bool> RotateSecretAsync(Guid tenantId, string clientId, string clientSecret, CancellationToken cancellationToken = default)
	{
		if (await FindAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is not { } application)
		{
			return false;
		}

		await applications.UpdateAsync(application, clientSecret, cancellationToken).ConfigureAwait(false);
		return true;
	}

	public async Task<bool> DeleteAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default)
	{
		if (await FindAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is not { } application)
		{
			return false;
		}

		await applications.DeleteAsync(application, cancellationToken).ConfigureAwait(false);
		return true;
	}

	private Task<OidcApplication?> FindAsync(Guid tenantId, string clientId, CancellationToken cancellationToken) =>
		TenantClients(tenantId).FirstOrDefaultAsync(a => a.ClientId == clientId, cancellationToken);

	private static OpenIddictApplicationDescriptor Describe(string clientId, ProductClientAccess access)
	{
		var descriptor = new OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientType = ClientTypes.Confidential,
			DisplayName = access.Name,
		};
		descriptor.Permissions.Add(Permissions.Endpoints.Token);
		descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);

		foreach (var scope in access.Scopes)
		{
			descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
		}

		return descriptor;
	}

	private static string? JoinRoles(IReadOnlyList<string> roles) => roles.Count == 0 ? null : string.Join(' ', roles);

	// CreatedAt fica nulo: a entidade do OpenIddict EF Core não tem CreationDate e a spec não pede coluna nova.
	private static ProductClientDto ToDto(OidcApplication application) =>
		new(
			application.ClientId!,
			application.Name!,
			[.. ParsePermissions(application.Permissions)
				.Where(p => p.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
				.Select(p => p[Permissions.Prefixes.Scope.Length..])
				.Order(StringComparer.Ordinal)],
			application.Roles?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
			null);

	private static string[] ParsePermissions(string? json) =>
		string.IsNullOrEmpty(json) ? [] : System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
}
