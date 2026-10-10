using Secco.SecureGate.Application.Permissions;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Permissões órfãs retiradas de um perfil.</summary>
/// <param name="Role">Nome do perfil.</param>
/// <param name="Removed">Permissões removidas, ordenadas.</param>
public sealed record RoleUnknownPermissionsDto(string Role, IReadOnlyList<string> Removed);

/// <summary>Resumo da limpeza de órfãs de um tenant.</summary>
/// <param name="RolesChanged">Perfis que perderam ao menos uma permissão.</param>
/// <param name="PermissionsRemoved">Total de permissões removidas (somadas por perfil).</param>
/// <param name="Roles">Detalhe por perfil alterado — perfil sem órfã não aparece.</param>
public sealed record RemovedUnknownPermissionsDto(int RolesChanged, int PermissionsRemoved, IReadOnlyList<RoleUnknownPermissionsDto> Roles);

/// <summary>
/// Remove, nos perfis de UM tenant, as permissões que nenhum catálogo de produto declara (ADR-0039).
/// Ato explícito do admin sobre um tenant por vez — publicar catálogo nunca faz isso sozinho, e nunca
/// atravessa tenants. Perfis reservados não são tocados (seus poderes não vêm de permissão gravada).
/// Idempotente: a segunda chamada não encontra mais órfãs.
/// </summary>
public sealed class RemoveUnknownRolePermissionsHandler(IRoleRepository roles, IPermissionCatalogRepository catalog)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota — o único que a limpeza alcança.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<RemovedUnknownPermissionsDto>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		if (!await roles.TenantExistsAsync(tenantId, cancellationToken).ConfigureAwait(false))
		{
			return SecureGateErrors.Tenants.NotFound;
		}

		var candidates = (await roles.ListRolesAsync(tenantId, cancellationToken).ConfigureAwait(false))
			.Where(role => !RoleInputRules.IsReservedName(role.Name) && role.Permissions.Count > 0)
			.ToList();

		var unknown = (await catalog
				.FindUnknownAsync(candidates.SelectMany(role => role.Permissions), cancellationToken)
				.ConfigureAwait(false))
			.ToHashSet(StringComparer.Ordinal);

		var changes = candidates
			.Select(role => new RoleUnknownPermissionsDto(
				role.Name,
				[.. role.Permissions.Where(unknown.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]))
			.Where(change => change.Removed.Count > 0)
			.ToList();

		if (changes.Count > 0)
		{
			await roles.RemovePermissionsAsync(tenantId, changes, cancellationToken).ConfigureAwait(false);
		}

		return new RemovedUnknownPermissionsDto(changes.Count, changes.Sum(change => change.Removed.Count), changes);
	}
}
