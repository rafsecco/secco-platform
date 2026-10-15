using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Members;

/// <summary>Comando de suspensão de vínculo.</summary>
/// <param name="TenantId">Tenant da rota.</param>
/// <param name="UserId">Pessoa.</param>
/// <param name="CallerSubject">O <c>sub</c> de quem chama — nunca vindo do corpo da requisição.</param>
public sealed record SuspendTenantMemberCommand(Guid TenantId, Guid UserId, string? CallerSubject);

/// <summary>
/// Suspende o vínculo (ADR-0040): a pessoa deixa de receber token para o tenant, sem perder o histórico.
/// Suspender DE FATO revoga as sessões; a chamada idempotente sem efeito não derruba ninguém.
/// </summary>
public sealed class SuspendTenantMemberHandler(
	ITenantRepository tenants,
	IUserDirectory userDirectory,
	ITenantMembershipRepository memberships,
	ISessionRevoker revoker)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(SuspendTenantMemberCommand command, CancellationToken cancellationToken = default)
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

		// O vínculo do dono não se suspende (a regra vale pela coluna, não pela linha)
		if (ownerTenantId == command.TenantId)
		{
			return Result.Failure(SecureGateErrors.Members.OwnerMembership);
		}

		if (MemberRules.IsSelf(command.CallerSubject, command.UserId))
		{
			return Result.Failure(SecureGateErrors.Members.CannotChangeOwnMembership);
		}

		var membership = await memberships.GetAsync(command.TenantId, command.UserId, cancellationToken)
			.ConfigureAwait(false);

		if (membership is null)
		{
			return Result.Failure(SecureGateErrors.Members.MembershipNotFound);
		}

		if (!membership.IsActive)
		{
			return Result.Success();
		}

		membership.Suspend();
		await memberships.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await revoker.RevokeAllAsync(command.UserId, SessionRevocationReason.MembershipEnded, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success();
	}
}
