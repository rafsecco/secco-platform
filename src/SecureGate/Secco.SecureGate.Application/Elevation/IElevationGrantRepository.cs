using Secco.SecureGate.Domain.Elevation;

namespace Secco.SecureGate.Application.Elevation;

/// <summary>
/// Persistência da concessão de elevação (ADR-0031). ASSINATURA FIXA: o token exchange
/// (implementado em paralelo, fora deste módulo) compila contra esta interface — não alterar
/// os métodos existentes.
/// </summary>
public interface IElevationGrantRepository
{
	/// <summary>Busca a concessão de um usuário (no máximo uma, por índice único em <c>id_fk_user</c>).</summary>
	/// <param name="userId">Usuário dono da concessão.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<ElevationGrant?> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Insere a concessão se for nova, ou efetiva a atualização de uma instância já obtida por
	/// <see cref="GetByUserAsync"/> e mutada (upsert idempotente).
	/// </summary>
	/// <param name="grant">Concessão a persistir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task UpsertAsync(ElevationGrant grant, CancellationToken cancellationToken = default);

	/// <summary>Remove a concessão do usuário, se existir.</summary>
	/// <param name="userId">Usuário dono da concessão.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>true</c> se havia concessão e foi removida; <c>false</c> se não havia (idempotente).</returns>
	Task<bool> RemoveAsync(Guid userId, CancellationToken cancellationToken = default);
}
