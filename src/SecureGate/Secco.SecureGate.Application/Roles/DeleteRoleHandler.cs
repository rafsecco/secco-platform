using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Exclui um perfil vazio do tenant. Perfis reservados nunca são excluídos.</summary>
public sealed class DeleteRoleHandler(IRoleRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="roleName">Nome do perfil (rota).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, string? roleName, CancellationToken cancellationToken = default)
	{
		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return Result.Failure(SecureGateErrors.Roles.NameInvalid);
		}

		// Antes do banco: a lista de reservados é pública, e o de operador sustenta a instalação
		if (RoleInputRules.IsReservedName(name))
		{
			return Result.Failure(SecureGateErrors.Roles.CannotDeleteReserved);
		}

		return await repository.DeleteRoleAsync(tenantId, name, cancellationToken).ConfigureAwait(false) switch
		{
			DeleteRoleOutcome.Deleted => Result.Success(),
			DeleteRoleOutcome.HasMembers => Result.Failure(SecureGateErrors.Roles.HasMembers),
			_ => Result.Failure(SecureGateErrors.Roles.NotFound),
		};
	}
}
