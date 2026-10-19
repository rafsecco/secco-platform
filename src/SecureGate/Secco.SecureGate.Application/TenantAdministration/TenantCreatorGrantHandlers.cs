using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>
/// Concede a criação de tenant (ADR-0047). Idempotente pelo efeito (ADR-0034). Só o operador chega aqui —
/// a autoridade de admin de tenant não concede nada a ninguém, nem a si mesma.
/// </summary>
public sealed class GrantTenantCreatorHandler(ITenantAdminGrantRepository grants)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="userId">Pessoa, de qualquer tenant.</param>
	/// <param name="grantedBy">Sub de quem concede — da claim do chamador, nunca do corpo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid userId, string grantedBy, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrEmpty(grantedBy) || grantedBy.Length > TenantAdminGrant.GrantedByMaxLength)
		{
			return Result.Failure(SecureGateErrors.TenantAdmins.GrantedByRequired);
		}

		if (!await grants.UserExistsAsync(userId, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.TenantAdmins.UserNotFound);
		}

		await grants.AddCreatorGrantIfMissingAsync(
			new TenantCreatorGrant(userId, grantedBy, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}

/// <summary>
/// Revoga a criação de tenant (ADR-0047). Idempotente. A administração dos tenants já criados continua: tirá-la
/// é ato separado do operador, tenant a tenant.
/// </summary>
public sealed class RevokeTenantCreatorHandler(ITenantAdminGrantRepository grants)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid userId, CancellationToken cancellationToken = default)
	{
		await grants.RemoveCreatorGrantAsync(userId, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}

/// <summary>Lista as pessoas que podem criar tenant, com quantos cada uma criou (ADR-0047).</summary>
public sealed class ListTenantCreatorsHandler(ITenantAdminGrantRepository grants)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<TenantCreatorDto>>> HandleAsync(CancellationToken cancellationToken = default) =>
		Result.Success(await grants.ListCreatorsAsync(cancellationToken).ConfigureAwait(false));
}
