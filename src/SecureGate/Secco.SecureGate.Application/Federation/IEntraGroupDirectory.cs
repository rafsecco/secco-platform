using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Federation;

/// <summary>
/// Porta de leitura do diretório federado via Microsoft Graph (ADR-0036). Implementação na
/// Infrastructure fala HTTP direto com o Graph — nada de <c>Microsoft.Graph</c> nem
/// <c>Microsoft.Identity.Client</c>: a superfície usada (token de aplicação + listar grupos) não
/// justifica as duas dependências.
/// </summary>
public interface IEntraGroupDirectory
{
	/// <summary>
	/// Lista os grupos do diretório de uma empresa cliente (o directory id da federação dela).
	/// </summary>
	/// <param name="directoryId">Directory id do Entra ID do tenant (<c>TenantFederation.DirectoryId</c>).</param>
	/// <param name="nameFilter">
	/// Filtro por nome, já sanitizado pelo chamador (<see cref="EntraGroupNameFilter"/>) — esta
	/// porta não valida de novo, só usa.
	/// </param>
	/// <param name="pageToken">Token opaco da página anterior; <c>null</c> para a primeira página.</param>
	/// <param name="pageSize">Itens por página, já normalizado pelo chamador.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>
	/// Falha com <see cref="ErrorType.Forbidden"/> se o Graph recusar por falta de consentimento de
	/// aplicação (<c>Authorization_RequestDenied</c>), ou <see cref="ErrorType.Unavailable"/> se o
	/// serviço não responder — nunca vaza o corpo do erro do Graph ao chamador (ADR-0020).
	/// </returns>
	Task<Result<EntraGroupPage>> ListGroupsAsync(
		Guid directoryId,
		string? nameFilter,
		string? pageToken,
		int pageSize,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Ids (object id) dos USUÁRIOS que pertencem ao grupo, de forma transitiva (grupo aninhado conta) — a leitura
	/// do ciclo de reconciliação (ADR-0055). Percorre a paginação INTEIRA: ou devolve a lista completa, ou falha.
	/// Resposta parcial nunca vira sucesso — uma lista truncada retiraria perfis de quem ficou fora dela.
	/// </summary>
	/// <param name="directoryId">Directory id do Entra ID do tenant.</param>
	/// <param name="groupId">Id do grupo (Guid; nunca texto livre em URL).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>
	/// Os object ids, sem repetição. Falha com <see cref="ErrorType.Forbidden"/> (sem consentimento),
	/// <see cref="ErrorType.NotFound"/> (grupo inexistente) ou <see cref="ErrorType.Unavailable"/> (indisponível ou
	/// leitura incompleta).
	/// </returns>
	Task<Result<IReadOnlyList<Guid>>> GetGroupMemberIdsAsync(
		Guid directoryId, Guid groupId, CancellationToken cancellationToken = default);

	/// <summary>
	/// A quais dos grupos informados o usuário pertence, de forma transitiva — a leitura do login federado
	/// (ADR-0055). Consulta o Graph em lotes de 20 (o limite do <c>checkMemberGroups</c>); se qualquer lote falhar,
	/// a resposta inteira falha — nada parcial.
	/// </summary>
	/// <param name="directoryId">Directory id do Entra ID do tenant.</param>
	/// <param name="objectId">Object id (<c>oid</c>) do usuário no diretório.</param>
	/// <param name="groupIds">Grupos a conferir (os mapeados do tenant).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns>O subconjunto de <paramref name="groupIds"/> a que o usuário pertence.</returns>
	Task<Result<IReadOnlySet<Guid>>> CheckMemberGroupsAsync(
		Guid directoryId, Guid objectId, IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken = default);
}
