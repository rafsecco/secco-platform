using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Elevation;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Elevation;

/// <summary>Comando de concessão/renovação de elevação (ADR-0031).</summary>
/// <param name="TenantId">Tenant do usuário, vindo da rota.</param>
/// <param name="UserId">Usuário a habilitar, vindo da rota.</param>
/// <param name="GrantedBy">Sub de quem concede — do claim do chamador autenticado, nunca do corpo.</param>
/// <param name="ExpiresAt">Expiração opcional; nulo = sem expiração.</param>
public sealed record GrantElevationCommand(Guid TenantId, Guid UserId, string GrantedBy, DateTimeOffset? ExpiresAt);

/// <summary>
/// Upsert idempotente da concessão de elevação (ADR-0031): sem linha aqui, não há elevação —
/// é a AUTORIDADE que falta ao mecanismo de troca de token do núcleo (RFC 8693). Recusa
/// expiração no passado e usuário que não pertence ao tenant da rota, com a MESMA resposta de
/// usuário inexistente (ADR-0020).
/// </summary>
public sealed class GrantElevationHandler(IUserDirectory userDirectory, IElevationGrantRepository repository)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando de concessão/renovação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(GrantElevationCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrEmpty(command.GrantedBy))
		{
			// Defesa em profundidade (ADR-0020): o gate de scope já exige chamador autenticado,
			// então a claim 'sub' ausente aqui seria token malformado, não fluxo normal.
			return Result.Failure(SecureGateErrors.Elevation.GrantedByRequired);
		}

		var now = DateTimeOffset.UtcNow;

		if (command.ExpiresAt is { } expiresAt && expiresAt <= now)
		{
			return Result.Failure(SecureGateErrors.Elevation.ExpiresAtInPast);
		}

		if (!await ElevationUserGuard
				.BelongsToTenantAsync(userDirectory, command.TenantId, command.UserId, cancellationToken)
				.ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Elevation.UserNotFound);
		}

		var existing = await repository.GetByUserAsync(command.UserId, cancellationToken).ConfigureAwait(false);

		if (existing is null)
		{
			var grant = new ElevationGrant(command.UserId, command.TenantId, command.GrantedBy, command.ExpiresAt, now);
			await repository.UpsertAsync(grant, cancellationToken).ConfigureAwait(false);
		}
		else
		{
			existing.Renew(command.ExpiresAt, command.GrantedBy, now);
			await repository.UpsertAsync(existing, cancellationToken).ConfigureAwait(false);
		}

		return Result.Success();
	}
}
