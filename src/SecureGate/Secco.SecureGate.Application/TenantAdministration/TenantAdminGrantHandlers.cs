using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.TenantAdministration;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.TenantAdministration;

/// <summary>Comando de concessão de admin de tenant (ADR-0044).</summary>
/// <param name="TenantId">Tenant administrado, vindo da rota.</param>
/// <param name="UserId">Pessoa, vinda da rota.</param>
/// <param name="GrantedBy">Sub de quem concede — da claim do chamador, nunca do corpo.</param>
public sealed record GrantTenantAdminCommand(Guid TenantId, Guid UserId, string GrantedBy);

/// <summary>
/// Concede a autoridade de admin de tenant (ADR-0044). Idempotente pelo efeito (ADR-0034): conceder de
/// novo não muda nada. A pessoa pode ser de qualquer tenant e não precisa ser membro do administrado;
/// o tenant de plataforma nunca é administrável.
/// </summary>
public sealed class GrantTenantAdminHandler(ITenantRepository tenants, ITenantAdminGrantRepository grants)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Comando.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(GrantTenantAdminCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (string.IsNullOrEmpty(command.GrantedBy) || command.GrantedBy.Length > TenantAdminGrant.GrantedByMaxLength)
		{
			return Result.Failure(SecureGateErrors.TenantAdmins.GrantedByRequired);
		}

		// Antes de qualquer consulta: a resposta não depende de o tenant de plataforma "existir" no banco
		if (command.TenantId == SecureGatePlatform.TenantId)
		{
			return Result.Failure(SecureGateErrors.TenantAdmins.PlatformTenantNotAdministrable);
		}

		if (await tenants.GetByIdAsync(command.TenantId, cancellationToken).ConfigureAwait(false) is null)
		{
			return Result.Failure(SecureGateErrors.Tenants.NotFound);
		}

		if (!await grants.UserExistsAsync(command.UserId, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.TenantAdmins.UserNotFound);
		}

		if (!await grants.ExistsAsync(command.TenantId, command.UserId, cancellationToken).ConfigureAwait(false))
		{
			await grants.AddAsync(
				new TenantAdminGrant(command.UserId, command.TenantId, command.GrantedBy, DateTimeOffset.UtcNow),
				cancellationToken).ConfigureAwait(false);
		}

		return Result.Success();
	}
}

/// <summary>
/// Revoga a autoridade de admin de tenant (ADR-0044). Idempotente: sem concessão, <c>204</c> do mesmo
/// jeito. Vale na requisição seguinte da pessoa — a conferência é no cadastro, não no token.
/// </summary>
public sealed class RevokeTenantAdminHandler(ITenantRepository tenants, ITenantAdminGrantRepository grants)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
	{
		if (await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
		{
			return Result.Failure(SecureGateErrors.Tenants.NotFound);
		}

		await grants.RemoveAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}

/// <summary>Lista os admins de um tenant (ADR-0044).</summary>
public sealed class ListTenantAdminsHandler(ITenantRepository tenants, ITenantAdminGrantRepository grants)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant administrado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<TenantAdminDto>>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
	{
		if (await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is null)
		{
			return SecureGateErrors.Tenants.NotFound;
		}

		return Result.Success(await grants.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false));
	}
}

/// <summary>Os tenants que o chamador administra (ADR-0044) — de onde a aplicação monta a área administrativa.</summary>
public sealed class ListAdministeredTenantsHandler(ITenantAdminGrantRepository grants)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="userId">Chamador (claim <c>sub</c>).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<IReadOnlyList<TenantDto>>> HandleAsync(Guid userId, CancellationToken cancellationToken = default) =>
		Result.Success(await grants.ListTenantsForUserAsync(userId, cancellationToken).ConfigureAwait(false));
}
