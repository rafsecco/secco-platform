using Secco.LogStream.Domain.ApiCalls;
using Secco.SharedKernel.Pagination;

namespace Secco.LogStream.Application.ApiCalls;

/// <summary>Filtros da busca de chamadas de API. Todos opcionais; combinados com AND.</summary>
/// <param name="From">Ocorridas a partir deste momento (inclusive).</param>
/// <param name="To">Ocorridas até este momento (inclusive).</param>
/// <param name="IsSuccess">Sucesso/falha segundo o chamador.</param>
/// <param name="HttpMethod">Método HTTP exato (case-insensitive).</param>
/// <param name="UrlContains">Trecho contido na URL.</param>
/// <param name="ResponseStatusCode">Status HTTP exato.</param>
/// <param name="CorrelationId">Correlation id exato.</param>
/// <param name="Page">Paginação (1-based).</param>
public sealed record ApiCallLogSearchCriteria(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    bool? IsSuccess = null,
    string? HttpMethod = null,
    string? UrlContains = null,
    int? ResponseStatusCode = null,
    Guid? CorrelationId = null,
    PageRequest? Page = null)
{
    /// <summary>Paginação efetiva (default da plataforma quando não informada).</summary>
    public PageRequest EffectivePage => Page ?? PageRequest.Default;
}

/// <summary>Porta de persistência/consulta de chamadas de API — sempre no banco do tenant atual (ADR-0005).</summary>
public interface IApiCallLogRepository
{
    /// <summary>Persiste um registro de chamada.</summary>
    /// <param name="apiCallLog">Registro a persistir.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task AddAsync(ApiCallLog apiCallLog, CancellationToken cancellationToken = default);

    /// <summary>Busca um registro pelo identificador.</summary>
    /// <param name="id">Identificador do registro.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<ApiCallLog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Busca paginada com os filtros informados, mais recentes primeiro.</summary>
    /// <param name="criteria">Filtros e paginação.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<PagedResult<ApiCallLog>> SearchAsync(ApiCallLogSearchCriteria criteria, CancellationToken cancellationToken = default);
}
