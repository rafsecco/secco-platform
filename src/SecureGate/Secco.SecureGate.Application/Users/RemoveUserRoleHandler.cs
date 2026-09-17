using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Sessions;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Comando de remoção de perfil.</summary>
/// <param name="TenantId">Tenant da rota.</param>
/// <param name="UserId">Usuário alvo.</param>
/// <param name="RoleName">Nome do perfil (rota).</param>
/// <param name="CallerSubject">O <c>sub</c> de quem chama — nunca vindo do corpo da requisição.</param>
public sealed record RemoveUserRoleCommand(Guid TenantId, Guid UserId, string? RoleName, string? CallerSubject);

/// <summary>
/// Retira um usuário de um perfil (issue #26). Idempotente. No perfil de operador da instalação,
/// ninguém se remove e o último operador ativo não sai.
/// </summary>
public sealed class RemoveUserRoleHandler(IUserDirectory userDirectory, ISessionRevoker revoker)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(RemoveUserRoleCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var name = command.RoleName?.Trim() ?? string.Empty;

		if (!RoleInputRules.IsValidName(name))
		{
			return Result.Failure(SecureGateErrors.Roles.NameInvalid);
		}

		if (command.TenantId == SecureGatePlatform.TenantId
			&& string.Equals(name, SecureGatePlatform.OperatorRole, StringComparison.OrdinalIgnoreCase))
		{
			if (string.Equals(command.CallerSubject, command.UserId.ToString(), StringComparison.OrdinalIgnoreCase))
			{
				return Result.Failure(SecureGateErrors.Users.CannotRemoveSelfFromOperator);
			}

			if (await OperatorGuard.WouldLeaveNoActiveOperatorAsync(userDirectory, command.UserId, cancellationToken)
				.ConfigureAwait(false))
			{
				return Result.Failure(SecureGateErrors.Users.LastActiveOperator);
			}
		}

		var outcome = await userDirectory
			.RemoveRoleAsync(command.TenantId, command.UserId, name, cancellationToken)
			.ConfigureAwait(false);

		// Só uma remoção de fato encerra a sessão; a idempotente não derruba ninguém à toa
		if (outcome == RoleAssignmentOutcome.Done)
		{
			await revoker.RevokeAllAsync(command.UserId, SessionRevocationReason.RoleRemoved, cancellationToken)
				.ConfigureAwait(false);
		}

		return AddUserRoleHandler.ToResult(outcome);
	}
}
