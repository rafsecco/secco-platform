using System.Collections.Frozen;
using Secco.LogStream.Application.Ingestion;
using Secco.LogStream.Domain.ApiCalls;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application.ApiCalls;

/// <summary>Comando de registro de uma chamada de API externa.</summary>
/// <param name="Url">URL chamada. Obrigatória (URI absoluto).</param>
/// <param name="HttpMethod">Método HTTP. Obrigatório.</param>
/// <param name="IsSuccess">Sucesso segundo o chamador.</param>
/// <param name="RequestBody">Corpo da requisição (opcional; truncado no limite).</param>
/// <param name="RequestHeaders">Headers (opcional; sanitizados pela blocklist — ADR-0020).</param>
/// <param name="ResponseStatusCode">Status HTTP da resposta, quando houve.</param>
/// <param name="ResponseBody">Corpo da resposta (opcional; truncado no limite).</param>
/// <param name="DurationMs">Duração em milissegundos.</param>
/// <param name="ErrorMessage">Mensagem de erro, quando houver (truncada no limite de mensagem).</param>
/// <param name="CorrelationId">Correlation id da requisição de origem (populado pela borda).</param>
public sealed record CreateApiCallLogCommand(
    string? Url,
    string? HttpMethod,
    bool IsSuccess,
    string? RequestBody = null,
    IReadOnlyDictionary<string, string?>? RequestHeaders = null,
    int? ResponseStatusCode = null,
    string? ResponseBody = null,
    long? DurationMs = null,
    string? ErrorMessage = null,
    Guid? CorrelationId = null);

/// <summary>
/// Valida (ADR-0020: formato de URL, vocabulário de método, faixa de status), sanitiza
/// headers, trunca corpos e enfileira o registro — ingestão assíncrona (<c>202</c>).
/// </summary>
public sealed class CreateApiCallLogHandler(ILogIngestionQueue queue, LogStreamIngestionOptions options)
{
    private static readonly FrozenSet<string> KnownHttpMethods = new[]
    {
        "GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS", "TRACE", "CONNECT",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Executa o caso de uso.</summary>
    /// <param name="command">Comando de registro.</param>
    public Result<Guid> Handle(CreateApiCallLogCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Url))
        {
            return LogStreamErrors.ApiCalls.UrlRequired;
        }

        if (command.Url.Length > options.MaxUrlLength)
        {
            return LogStreamErrors.ApiCalls.UrlTooLong(options.MaxUrlLength);
        }

        if (!Uri.TryCreate(command.Url, UriKind.Absolute, out _))
        {
            return LogStreamErrors.ApiCalls.UrlMalformed;
        }

        if (string.IsNullOrWhiteSpace(command.HttpMethod) || !KnownHttpMethods.Contains(command.HttpMethod))
        {
            return LogStreamErrors.ApiCalls.MethodInvalid;
        }

        if (command.ResponseStatusCode is < 100 or > 599)
        {
            return LogStreamErrors.ApiCalls.StatusCodeOutOfRange;
        }

        if (command.DurationMs is < 0)
        {
            return LogStreamErrors.ApiCalls.DurationNegative;
        }

        var apiCallLog = new ApiCallLog(
            command.Url,
            command.HttpMethod,
            command.IsSuccess,
            BodyTruncator.Truncate(command.RequestBody, options.MaxBodyLength),
            HeaderSanitizer.SanitizeAndSerialize(command.RequestHeaders, options),
            command.ResponseStatusCode,
            BodyTruncator.Truncate(command.ResponseBody, options.MaxBodyLength),
            command.DurationMs,
            BodyTruncator.Truncate(command.ErrorMessage, options.MaxMessageLength),
            command.CorrelationId);

        return queue.TryEnqueue(apiCallLog) switch
        {
            EnqueueOutcome.Enqueued => apiCallLog.Id,
            EnqueueOutcome.QueueFull => LogStreamErrors.Ingestion.QueueFull,
            _ => LogStreamErrors.Ingestion.TenantNotResolved,
        };
    }
}
