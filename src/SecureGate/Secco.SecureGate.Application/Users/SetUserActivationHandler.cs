using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Comando de ativação ou desativação de um usuário.</summary>
/// <param name="TenantId">Tenant da rota.</param>
/// <param name="UserId">Usuário alvo.</param>
/// <param name="Active"><c>true</c> para ativar; <c>false</c> para desativar.</param>
/// <param name="CallerSubject">O <c>sub</c> de quem chama — nunca vindo do corpo da requisição.</param>
public sealed record SetUserActivationCommand(Guid TenantId, Guid UserId, bool Active, string? CallerSubject);

/// <summary>
/// Ativa ou desativa um usuário. Desativar é a alavanca que faltava para encerrar a sessão de um
/// usuário comprometido: com a renovação de token re-checando o estado da conta, a sessão termina
/// na próxima renovação — em até um TTL de access token.
/// </summary>
public sealed class SetUserActivationHandler(IUserDirectory userDirectory)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(SetUserActivationCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (!command.Active
			&& string.Equals(command.CallerSubject, command.UserId.ToString(), StringComparison.OrdinalIgnoreCase))
		{
			return Result.Failure(SecureGateErrors.Users.CannotDeactivateSelf);
		}

		var found = await userDirectory
			.SetActiveAsync(tenantId: command.TenantId, userId: command.UserId, command.Active, cancellationToken)
			.ConfigureAwait(false);

		return found ? Result.Success() : Result.Failure(SecureGateErrors.Users.NotFound);
	}
}
