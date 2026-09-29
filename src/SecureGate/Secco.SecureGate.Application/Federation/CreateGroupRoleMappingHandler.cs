using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Comando de cadastro de um mapeamento grupo→perfil (issue #28).</summary>
/// <param name="TenantId">Tenant dono do mapeamento.</param>
/// <param name="EntraGroupId">Id do grupo no Entra ID.</param>
/// <param name="EntraGroupDisplayName">Nome de exibição do grupo, para snapshot.</param>
/// <param name="RoleName">Nome do perfil a conceder.</param>
public sealed record CreateGroupRoleMappingCommand(
	Guid TenantId, Guid EntraGroupId, string? EntraGroupDisplayName, string? RoleName);

/// <summary>
/// Cadastra o mapeamento entre um grupo do diretório federado e um perfil do tenant (issue #28,
/// ADR-0036). Exige federação habilitada — mapear grupo sem federação não tem o que sincronizar.
/// Perfil reservado nunca é mapeável, mesma allowlist da atribuição manual (ADR-0023/0024/0031).
/// </summary>
public sealed class CreateGroupRoleMappingHandler(
	ITenantRepository tenants, IRoleRepository roles, IGroupRoleMappingRepository mappings)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de cadastro.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<GroupRoleMappingRecord>> HandleAsync(
		CreateGroupRoleMappingCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (command.EntraGroupId == Guid.Empty)
		{
			return Result.Failure<GroupRoleMappingRecord>(SecureGateErrors.Federation.EntraGroupIdRequired);
		}

		var displayName = command.EntraGroupDisplayName?.Trim() ?? string.Empty;

		if (displayName.Length is 0 or > TenantGroupRoleMapping.DisplayNameMaxLength)
		{
			return Result.Failure<GroupRoleMappingRecord>(SecureGateErrors.Federation.EntraGroupDisplayNameInvalid);
		}

		var roleName = command.RoleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(roleName))
		{
			return Result.Failure<GroupRoleMappingRecord>(SecureGateErrors.Roles.NameInvalid);
		}

		if (!RoleInputRules.IsAssignableToUsers(roleName))
		{
			return Result.Failure<GroupRoleMappingRecord>(SecureGateErrors.Users.RoleNotAssignable);
		}

		var federation = await tenants.GetFederationAsync(command.TenantId, cancellationToken).ConfigureAwait(false);

		if (federation is not { IsEnabled: true })
		{
			return Result.Failure<GroupRoleMappingRecord>(SecureGateErrors.Federation.NotEnabled);
		}

		if (await mappings.FindByGroupAsync(command.TenantId, command.EntraGroupId, cancellationToken).ConfigureAwait(false)
			is not null)
		{
			return Result.Failure<GroupRoleMappingRecord>(SecureGateErrors.Federation.GroupAlreadyMapped);
		}

		var role = await roles.FindRoleAsync(command.TenantId, roleName, cancellationToken).ConfigureAwait(false);

		if (role is null)
		{
			return Result.Failure<GroupRoleMappingRecord>(SecureGateErrors.Roles.NotFound);
		}

		var mapping = new TenantGroupRoleMapping(command.TenantId, command.EntraGroupId, displayName, role.Id);

		await mappings.AddAsync(mapping, cancellationToken).ConfigureAwait(false);
		await mappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return new GroupRoleMappingRecord(
			mapping.Id, mapping.EntraGroupId, mapping.EntraGroupDisplayName, mapping.RoleId, role.Name, mapping.CreatedAt);
	}
}
