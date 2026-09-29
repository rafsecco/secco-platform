using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Remove um mapeamento grupo→perfil (issue #28). Só desfaz o VÍNCULO — não retira o perfil de
/// quem já o tem por causa dele; essas atribuições ficam como estão, com a origem preservada
/// (nenhuma reconciliação existe nesta entrega para desfazê-las automaticamente).
/// </summary>
public sealed class DeleteGroupRoleMappingHandler(IGroupRoleMappingRepository mappings)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="mappingId">Mapeamento a remover.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(Guid tenantId, Guid mappingId, CancellationToken cancellationToken = default)
	{
		var mapping = await mappings.GetByIdAsync(tenantId, mappingId, cancellationToken).ConfigureAwait(false);

		if (mapping is null)
		{
			return Result.Failure(SecureGateErrors.Federation.MappingNotFound);
		}

		await mappings.RemoveAsync(mapping, cancellationToken).ConfigureAwait(false);
		await mappings.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}
}
