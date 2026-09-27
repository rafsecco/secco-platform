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
}
