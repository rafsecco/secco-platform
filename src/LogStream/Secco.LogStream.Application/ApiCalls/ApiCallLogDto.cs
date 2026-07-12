using Secco.LogStream.Domain.ApiCalls;

namespace Secco.LogStream.Application.ApiCalls;

/// <summary>Representação de leitura de uma chamada de API (headers já sanitizados na ingestão).</summary>
/// <param name="Id">Identificador do registro.</param>
/// <param name="Url">URL chamada.</param>
/// <param name="HttpMethod">Método HTTP.</param>
/// <param name="IsSuccess">Sucesso segundo o chamador.</param>
/// <param name="RequestBody">Corpo da requisição, quando enviado.</param>
/// <param name="RequestHeaders">Headers sanitizados em JSON, quando enviados.</param>
/// <param name="ResponseStatusCode">Status HTTP da resposta.</param>
/// <param name="ResponseBody">Corpo da resposta, quando enviado.</param>
/// <param name="DurationMs">Duração em milissegundos.</param>
/// <param name="ErrorMessage">Mensagem de erro, quando houver.</param>
/// <param name="CorrelationId">Correlation id de origem.</param>
/// <param name="CreatedAt">Momento da criação.</param>
public sealed record ApiCallLogDto(
    Guid Id,
    string Url,
    string HttpMethod,
    bool IsSuccess,
    string? RequestBody,
    string? RequestHeaders,
    int? ResponseStatusCode,
    string? ResponseBody,
    long? DurationMs,
    string? ErrorMessage,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    public static ApiCallLogDto FromEntity(ApiCallLog entity) =>
        new(entity.Id, entity.Url, entity.HttpMethod, entity.IsSuccess, entity.RequestBody,
            entity.RequestHeaders, entity.ResponseStatusCode, entity.ResponseBody,
            entity.DurationMs, entity.ErrorMessage, entity.CorrelationId, entity.CreatedAt);
}
