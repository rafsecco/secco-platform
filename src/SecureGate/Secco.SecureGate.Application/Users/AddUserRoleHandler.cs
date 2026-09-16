using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Atribui um perfil a um usuário que já existe (issue #26). Idempotente: repetir devolve sucesso, e
/// 404 significa sempre "perfil ou usuário não existe neste tenant", nunca "já estava assim".
/// </summary>
public sealed class AddUserRoleHandler(IUserDirectory userDirectory)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="roleName">Nome do perfil (rota).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, string? roleName, CancellationToken cancellationToken = default)
	{
		var name = roleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return Result.Failure(SecureGateErrors.Roles.NameInvalid);
		}

		if (!RoleInputRules.IsAssignableToUsers(name))
		{
			return Result.Failure(SecureGateErrors.Users.RoleNotAssignable);
		}

		return ToResult(await userDirectory.AddRoleAsync(tenantId, userId, name, cancellationToken).ConfigureAwait(false));
	}

	/// <summary>Traduz o resultado da persistência — compartilhado com a remoção.</summary>
	/// <param name="outcome">Resultado.</param>
	internal static Result ToResult(RoleAssignmentOutcome outcome) => outcome switch
	{
		RoleAssignmentOutcome.Done => Result.Success(),
		RoleAssignmentOutcome.UserNotFound => Result.Failure(SecureGateErrors.Users.NotFound),
		_ => Result.Failure(SecureGateErrors.Roles.NotFound),
	};
}
