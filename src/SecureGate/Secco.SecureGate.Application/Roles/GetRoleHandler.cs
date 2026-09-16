using Secco.SecureGate.Application.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>
/// Detalha um perfil. As permissões vêm da MESMA resolução que os produtos consultam — inclusive os
/// casos especiais de perfil reservado —, para a tela nunca mostrar uma segunda interpretação.
/// </summary>
public sealed class GetRoleHandler(IRoleRepository repository, GetRolePermissionsHandler permissions)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="roleName">Nome do perfil (rota).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<RoleDetailDto>> HandleAsync(Guid tenantId, string? roleName, CancellationToken cancellationToken = default)
	{
		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return SecureGateErrors.Roles.NameInvalid;
		}

		var role = await repository.FindRoleAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

		if (role is null)
		{
			return SecureGateErrors.Roles.NotFound;
		}

		var granted = await permissions.HandleAsync(tenantId, role.Name, cancellationToken).ConfigureAwait(false);

		return new RoleDetailDto(
			role.Name,
			granted.IsSuccess ? granted.Value : [],
			RoleInputRules.IsReservedName(role.Name),
			role.MemberCount);
	}
}
