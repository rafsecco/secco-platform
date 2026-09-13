using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Elevation;

/// <summary>
/// Leitura da concessão de elevação de um usuário, com o estado ativo/expirado calculado
/// (ADR-0031). Usuário que não pertence ao tenant da rota responde 404 igual a usuário
/// inexistente (ADR-0020); só depois disso um 404 distinto sinaliza "sem concessão".
/// </summary>
public sealed class GetElevationHandler(IUserDirectory userDirectory, IElevationGrantRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant do usuário, vindo da rota.</param>
	/// <param name="userId">Usuário dono da concessão, vindo da rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ElevationGrantDto>> HandleAsync(
		Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		if (!await ElevationUserGuard.BelongsToTenantAsync(userDirectory, tenantId, userId, cancellationToken)
				.ConfigureAwait(false))
		{
			return Result.Failure<ElevationGrantDto>(SecureGateErrors.Elevation.UserNotFound);
		}

		var grant = await repository.GetByUserAsync(userId, cancellationToken).ConfigureAwait(false);

		if (grant is null)
		{
			return Result.Failure<ElevationGrantDto>(SecureGateErrors.Elevation.GrantNotFound);
		}

		var now = DateTimeOffset.UtcNow;

		return Result.Success(new ElevationGrantDto(
			grant.UserId, grant.TenantId, grant.GrantedBy, grant.CreatedAt, grant.ExpiresAt, grant.IsActiveAt(now)));
	}
}
