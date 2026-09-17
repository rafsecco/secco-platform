using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Sessions;

/// <summary>"Encerrar sessões" pelo admin (ADR-0032). Usuário de outro tenant responde como inexistente.</summary>
public sealed class RevokeUserSessionsHandler(IUserDirectory userDirectory, ISessionRevoker revoker)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		if (!await userDirectory.BelongsToTenantAsync(tenantId, userId, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Users.NotFound);
		}

		await revoker.RevokeAllAsync(userId, SessionRevocationReason.AdminRequest, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
