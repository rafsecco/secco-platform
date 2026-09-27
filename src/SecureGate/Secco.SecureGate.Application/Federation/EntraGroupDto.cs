namespace Secco.SecureGate.Application.Federation;

/// <summary>Grupo do diretório federado, para o admin escolher o que vira perfil (issue #27).</summary>
/// <param name="Id">Identificador do grupo no Entra ID (imutável — é a chave de casamento, nunca o nome).</param>
/// <param name="DisplayName">Nome de exibição do grupo, como cadastrado no diretório do cliente.</param>
/// <param name="MemberCount">
/// Quantidade de membros, quando barato de obter (o Graph devolve via <c>$count</c>); <c>null</c>
/// quando não solicitado — a issue pede "se barato", não uma segunda chamada por grupo.
/// </param>
public sealed record EntraGroupDto(string Id, string DisplayName, int? MemberCount);

/// <summary>
/// Página de grupos do diretório. A paginação do Graph é por cursor (<c>@odata.nextLink</c>), não
/// por número de página — <see cref="NextPageToken"/> é opaco ao chamador: ele só o devolve na
/// próxima chamada, nunca o interpreta.
/// </summary>
/// <param name="Items">Grupos desta página.</param>
/// <param name="NextPageToken">Token da próxima página; <c>null</c> quando esta é a última.</param>
public sealed record EntraGroupPage(IReadOnlyList<EntraGroupDto> Items, string? NextPageToken);
