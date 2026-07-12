using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.LogStream.Domain.ApiCalls;

/// <summary>
/// Registro de uma chamada HTTP a uma API externa — diagnóstico de integrações.
/// Imutável após a criação. Headers chegam já <b>sanitizados</b> pela camada de aplicação
/// (blocklist de segredos, ADR-0020) e serializados como JSON.
/// </summary>
public sealed class ApiCallLog : BaseEntity
{
    private ApiCallLog()
    {
        // Construtor de rehidratação do EF Core
        Url = string.Empty;
        HttpMethod = string.Empty;
    }

    /// <summary>Cria um registro de chamada de API.</summary>
    /// <param name="url">URL chamada. Obrigatória.</param>
    /// <param name="httpMethod">Método HTTP, normalizado em maiúsculas. Obrigatório.</param>
    /// <param name="isSuccess">Sucesso segundo o chamador (falha de rede não tem status code).</param>
    /// <param name="requestBody">Corpo da requisição (já truncado pela aplicação), quando enviado.</param>
    /// <param name="requestHeaders">Headers sanitizados serializados em JSON, quando enviados.</param>
    /// <param name="responseStatusCode">Status HTTP da resposta, quando houve resposta.</param>
    /// <param name="responseBody">Corpo da resposta (já truncado), quando enviado.</param>
    /// <param name="durationMs">Duração da chamada em milissegundos.</param>
    /// <param name="errorMessage">Mensagem de erro, quando houver.</param>
    /// <param name="correlationId">Correlation id da requisição de origem, quando propagado.</param>
    /// <exception cref="DomainInvariantException">Se url ou método forem nulos/vazios.</exception>
    public ApiCallLog(
        string url,
        string httpMethod,
        bool isSuccess,
        string? requestBody = null,
        string? requestHeaders = null,
        int? responseStatusCode = null,
        string? responseBody = null,
        long? durationMs = null,
        string? errorMessage = null,
        Guid? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new DomainInvariantException("Um registro de chamada exige URL não vazia.");
        }

        if (string.IsNullOrWhiteSpace(httpMethod))
        {
            throw new DomainInvariantException("Um registro de chamada exige método HTTP não vazio.");
        }

        Url = url;
        HttpMethod = httpMethod.ToUpperInvariant();
        IsSuccess = isSuccess;
        RequestBody = requestBody;
        RequestHeaders = requestHeaders;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        DurationMs = durationMs;
        ErrorMessage = errorMessage;
        CorrelationId = correlationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>URL chamada (coluna <c>ds_url</c>).</summary>
    public string Url { get; private set; }

    /// <summary>Método HTTP em maiúsculas (coluna <c>ds_http_method</c>).</summary>
    public string HttpMethod { get; private set; }

    /// <summary>Sucesso segundo o chamador (coluna <c>fl_success</c>).</summary>
    public bool IsSuccess { get; private set; }

    /// <summary>Corpo da requisição (coluna <c>ds_request_body</c>).</summary>
    public string? RequestBody { get; private set; }

    /// <summary>Headers sanitizados em JSON (coluna <c>ds_request_headers</c>).</summary>
    public string? RequestHeaders { get; private set; }

    /// <summary>Status HTTP da resposta (coluna <c>nr_response_status_code</c>).</summary>
    public int? ResponseStatusCode { get; private set; }

    /// <summary>Corpo da resposta (coluna <c>ds_response_body</c>).</summary>
    public string? ResponseBody { get; private set; }

    /// <summary>Duração em milissegundos (coluna <c>nr_duration_ms</c>).</summary>
    public long? DurationMs { get; private set; }

    /// <summary>Mensagem de erro (coluna <c>ds_error_message</c>).</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Correlation id de origem (coluna <c>correlation_id</c>).</summary>
    public Guid? CorrelationId { get; private set; }

    /// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
    public DateTimeOffset CreatedAt { get; private set; }
}
