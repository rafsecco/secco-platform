namespace Secco.SecureGate.Application.Federation;

/// <summary>Lista os mapeamentos grupo→perfil de um tenant (issue #28).</summary>
public sealed class ListGroupRoleMappingsHandler(IGroupRoleMappingRepository mappings)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public Task<IReadOnlyList<GroupRoleMappingRecord>> HandleAsync(
		Guid tenantId, CancellationToken cancellationToken = default) =>
		mappings.ListAsync(tenantId, cancellationToken);
}
