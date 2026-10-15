using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;
using Secco.SecureGate.Domain.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Members;

/// <summary>
/// Inclui ou reativa o vínculo de uma pessoa com o tenant (ADR-0040). Idempotente (ADR-0034): o estado
/// final é "vínculo ativo", qualquer que fosse o ponto de partida.
/// </summary>
public sealed class SetTenantMemberHandler(
	ITenantRepository tenants,
	IUserDirectory userDirectory,
	ITenantMembershipRepository memberships)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		if (await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
		{
			return Result.Failure(SecureGateErrors.Tenants.NotFound);
		}

		if (await userDirectory.GetOwnerTenantAsync(userId, cancellationToken).ConfigureAwait(false) is not { } ownerTenantId)
		{
			return Result.Failure(SecureGateErrors.Members.UserNotFound);
		}

		if (MemberRules.IsForeignToPlatform(tenantId, ownerTenantId))
		{
			return Result.Failure(SecureGateErrors.Members.PlatformTenantForeignUser);
		}

		// O dono já é membro ativo pela coluna: nada a fazer
		if (ownerTenantId == tenantId)
		{
			return Result.Success();
		}

		var existing = await memberships.GetAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);

		if (existing is null)
		{
			// Corrida com outra inclusão: perder a corrida é sucesso se o vínculo ficou ativo
			if (await memberships
				.TryAddAsync(new TenantMembership(userId, tenantId, DateTimeOffset.UtcNow), cancellationToken)
				.ConfigureAwait(false))
			{
				return Result.Success();
			}

			existing = await memberships.GetAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
		}

		if (existing is { IsActive: false })
		{
			existing.Reactivate();
			await memberships.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}

		return Result.Success();
	}
}
