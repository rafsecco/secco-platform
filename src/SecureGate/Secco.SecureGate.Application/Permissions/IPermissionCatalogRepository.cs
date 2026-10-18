using Secco.SharedKernel.Authorization;

namespace Secco.SecureGate.Application.Permissions;

/// <summary>Porta de persistência do catálogo de permissões da plataforma (ADR-0039).</summary>
public interface IPermissionCatalogRepository
{
	/// <summary>
	/// Substitui o conjunto de permissões do produto numa única transação: remove as ausentes,
	/// atualiza as de descrição diferente e insere as novas. Entrada igual não é tocada (nem o
	/// <c>PublishedAt</c>). Nunca toca perfil.
	/// </summary>
	/// <param name="product">Produto já validado.</param>
	/// <param name="permissions">Permissões já validadas, sem nome repetido.</param>
	/// <param name="now">Instante da publicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>Os nomes que JÁ existem no catálogo de OUTROS produtos (colisão visível).</returns>
	Task<IReadOnlyList<string>> ReplaceAsync(
		string product,
		IReadOnlyList<SeccoPermissionDefinition> permissions,
		DateTimeOffset now,
		CancellationToken cancellationToken = default);

	/// <summary>Lista o catálogo de todos os produtos que já publicaram.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<ProductPermissionCatalogDto>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Lê o catálogo de um produto; <c>null</c> se ele nunca publicou.</summary>
	/// <param name="product">Produto já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<ProductPermissionCatalogDto?> GetAsync(string product, CancellationToken cancellationToken = default);

	/// <summary>
	/// Dos nomes informados, devolve os que NENHUM produto declara (comparação ordinal), nem como nome
	/// exato nem como instância de uma família (ADR-0043).
	/// </summary>
	/// <param name="names">Nomes a conferir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<string>> FindUnknownAsync(IEnumerable<string> names, CancellationToken cancellationToken = default);
}
