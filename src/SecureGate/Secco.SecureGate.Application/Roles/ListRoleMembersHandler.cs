using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Lista, paginados, os membros de um perfil do tenant.</summary>
public sealed class ListRoleMembersHandler(IRoleRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="roleName">Nome do perfil (rota).</param>
	/// <param name="page">Página pedida (já limitada pelo SharedKernel).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PagedResult<RoleMemberDto>>> HandleAsync(
		Guid tenantId,
		string? roleName,
		PageRequest page,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(page);

		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return SecureGateErrors.Roles.NameInvalid;
		}

		// O perfil é localizado no tenant ANTES: a consulta de membros só recebe um id que já passou
		// pelo isolamento, nunca o nome cru
		var role = await repository.FindRoleAsync(tenantId, name, cancellationToken).ConfigureAwait(false);

		if (role is null)
		{
			return SecureGateErrors.Roles.NotFound;
		}

		var members = await repository.ListMembersAsync(role.Id, page, cancellationToken).ConfigureAwait(false);
		var now = DateTimeOffset.UtcNow;

		return PagedResult.Create<RoleMemberDto>(
			[.. members.Items.Select(member => new RoleMemberDto(
				member.UserId,
				member.Email,
				UserStatuses.From(member.LockoutEnabled, member.LockoutEnd, now)))],
			page,
			members.TotalCount);
	}
}
