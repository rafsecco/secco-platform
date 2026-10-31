using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Marca e desmarca o <b>grupo de provisionamento</b> do tenant (ADR-0055): o ato explícito do operador que passa ao
/// diretório a decisão de quem tem conta. Só um mapeamento por tenant pode ser o grupo de provisionamento
/// (garantido também por índice único no banco); desmarcá-lo desliga criar, adotar, desativar e reativar. As duas
/// operações são idempotentes (ADR-0034): o `PUT` repetido e o `DELETE` repetido terminam no mesmo estado.
/// </summary>
public sealed class SetProvisioningGroupHandler(IGroupRoleMappingRepository mappings)
{
	/// <summary>Marca o mapeamento como o grupo de provisionamento do tenant.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="mappingId">Mapeamento a marcar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> MarkAsync(Guid tenantId, Guid mappingId, CancellationToken cancellationToken = default)
	{
		var mapping = await mappings.GetByIdAsync(tenantId, mappingId, cancellationToken).ConfigureAwait(false);

		if (mapping is null)
		{
			return Result.Failure(SecureGateErrors.Federation.MappingNotFound);
		}

		if (mapping.IsProvisioningGroup)
		{
			return Result.Success();
		}

		var current = await mappings.FindProvisioningAsync(tenantId, cancellationToken).ConfigureAwait(false);

		if (current is not null && current.Id != mapping.Id)
		{
			return Result.Failure(SecureGateErrors.Federation.ProvisioningGroupAlreadySet);
		}

		mapping.MarkAsProvisioningGroup();

		return await mappings.TrySaveProvisioningChangeAsync(cancellationToken).ConfigureAwait(false)
			? Result.Success()
			: Result.Failure(SecureGateErrors.Federation.ProvisioningGroupAlreadySet);
	}

	/// <summary>Desmarca o mapeamento: o provisionamento e a desativação pelo diretório deixam de valer no tenant.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="mappingId">Mapeamento a desmarcar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> UnmarkAsync(Guid tenantId, Guid mappingId, CancellationToken cancellationToken = default)
	{
		var mapping = await mappings.GetByIdAsync(tenantId, mappingId, cancellationToken).ConfigureAwait(false);

		if (mapping is null)
		{
			return Result.Failure(SecureGateErrors.Federation.MappingNotFound);
		}

		if (!mapping.IsProvisioningGroup)
		{
			return Result.Success();
		}

		mapping.UnmarkAsProvisioningGroup();
		await mappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
