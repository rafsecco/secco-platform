using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Members;

/// <summary>Comando de remoção de vínculo.</summary>
/// <param name="TenantId">Tenant da rota.</param>
/// <param name="UserId">Pessoa.</param>
/// <param name="CallerSubject">O <c>sub</c> de quem chama — nunca vindo do corpo da requisição.</param>
public sealed record RemoveTenantMemberCommand(Guid TenantId, Guid UserId, string? CallerSubject);

/// <summary>
/// Remove o vínculo E os perfis da pessoa naquele tenant (ADR-0040), e só neles. Idempotente (ADR-0034):
/// quem não era membro também responde <c>204</c>; só a remoção de fato revoga as sessões.
/// </summary>
public sealed class RemoveTenantMemberHandler(
	ITenantRepository tenants,
	IUserDirectory userDirectory,
	ITenantMembershipRepository memberships,
	ISessionRevoker revoker)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(RemoveTenantMemberCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (await tenants.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false) is null)
		{
			return Result.Failure(SecureGateErrors.Tenants.NotFound);
		}

		if (await userDirectory.GetOwnerTenantAsync(command.UserId, cancellationToken).ConfigureAwait(false)
			is not { } ownerTenantId)
		{
			return Result.Failure(SecureGateErrors.Members.UserNotFound);
		}

		if (ownerTenantId == command.TenantId)
		{
			return Result.Failure(SecureGateErrors.Members.OwnerMembership);
		}

		if (MemberRules.IsSelf(command.CallerSubject, command.UserId))
		{
			return Result.Failure(SecureGateErrors.Members.CannotChangeOwnMembership);
		}

		var removed = await memberships.RemoveAsync(command.TenantId, command.UserId, cancellationToken)
			.ConfigureAwait(false);

		if (removed)
		{
			await revoker.RevokeAllAsync(command.UserId, SessionRevocationReason.MembershipEnded, cancellationToken)
				.ConfigureAwait(false);
		}

		return Result.Success();
	}
}
