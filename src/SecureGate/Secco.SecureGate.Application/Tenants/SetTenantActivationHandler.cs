using Secco.SecureGate.Application.TenantAdministration;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>
/// Ativa/desativa um tenant. Desativar remove o tenant do catálogo servido aos produtos —
/// a resolução de tenant para de funcionar em no máximo um TTL de cache dos consumidores.
/// </summary>
public sealed class SetTenantActivationHandler(ITenantRepository repository, ITenantAdminGrantRepository grants)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="id">Identificador do tenant.</param>
	/// <param name="active"><c>true</c> para ativar; <c>false</c> para desativar.</param>
	/// <param name="byTenantAdmin">
	/// Pedido pela autoridade de admin de tenant (ADR-0047): desativar um tenant dono de conta administrativa
	/// bloquearia essa pessoa em todos os sistemas (ADR-0040) — só o operador faz isso.
	/// </param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		Guid id, bool active, bool byTenantAdmin, CancellationToken cancellationToken = default)
	{
		// Os operadores da instalação vivem no tenant de plataforma. Desativado, eles deixam de renovar
		// token — e reativar exige token de operador. Não há caminho de volta pela API.
		if (!active && id == SecureGatePlatform.TenantId)
		{
			return Result.Failure(SecureGateErrors.Tenants.PlatformTenantCannotBeDeactivated);
		}

		var tenant = await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

		if (tenant is null)
		{
			return Result.Failure(SecureGateErrors.Tenants.NotFound);
		}

		if (!active
			&& byTenantAdmin
			&& await grants.OwnsAdministrativeAccountAsync(id, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.TenantAdmins.TenantOwnsAdministrativeAccount);
		}

		if (active)
		{
			tenant.Activate();
		}
		else
		{
			tenant.Deactivate();
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
