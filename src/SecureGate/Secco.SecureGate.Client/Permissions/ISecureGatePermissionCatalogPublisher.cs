using Secco.SharedKernel.Authorization;

namespace Secco.SecureGate.Client.Permissions;

/// <summary>
/// Publica no SecureGate o catálogo de permissões de um produto (ADR-0039). Chamado no <c>migrate</c>
/// do produto, depois do seed de referência — nunca no startup das réplicas.
/// </summary>
public interface ISecureGatePermissionCatalogPublisher
{
	/// <summary>
	/// Substitui o catálogo do produto (PUT idempotente). Sem a seção <c>Secco:SecureGate</c> não faz
	/// nada e registra o pulo; qualquer falha com a conexão configurada lança — o <c>migrate</c> falha
	/// de forma visível em vez de seguir sem catálogo.
	/// </summary>
	/// <param name="product">Produto em kebab-case (o mesmo do escopo <c>permissions:&lt;produto&gt;</c>).</param>
	/// <param name="permissions">Permissões do produto (nome e descrição).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task PublishAsync(string product, IReadOnlyList<SeccoPermissionDefinition> permissions, CancellationToken cancellationToken = default);
}
