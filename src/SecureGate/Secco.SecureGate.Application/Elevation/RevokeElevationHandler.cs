using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Elevation;

/// <summary>
/// Revogação idempotente da concessão de elevação (ADR-0031): bloqueia a PRÓXIMA troca de
/// token imediatamente (o token já emitido segue valendo até expirar — janela de até um TTL,
/// mesmo compromisso aceito pela ADR-0021 para revogação de permissão).
/// </summary>
public sealed class RevokeElevationHandler(IUserDirectory userDirectory, IElevationGrantRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant do usuário, vindo da rota.</param>
	/// <param name="userId">Usuário dono da concessão, vindo da rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		if (!await ElevationUserGuard.BelongsToTenantAsync(userDirectory, tenantId, userId, cancellationToken)
				.ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Elevation.UserNotFound);
		}

		// Idempotente por design (ADR-0031): existia ou não, o chamador só precisa saber que,
		// dali em diante, não há concessão — o retorno booleano do repositório não altera a resposta.
		await repository.RemoveAsync(userId, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
