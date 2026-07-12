using Secco.LogStream.Domain.LogEntries;

namespace Secco.LogStream.Api.Requests;

/// <summary>Payload de criação de um registro de log.</summary>
/// <param name="Level">Severidade do registro.</param>
/// <param name="Message">Mensagem do log. Obrigatória.</param>
/// <param name="StackTrace">Stack trace associado, quando houver.</param>
public sealed record CreateLogEntryRequest(LogEntryLevel Level, string? Message, string? StackTrace = null);

/// <summary>Resposta de ingestão aceita: o Id é definitivo (Guid v7 gerado antes da persistência).</summary>
/// <param name="Id">Identificador do registro aceito.</param>
public sealed record LogEntryAcceptedResponse(Guid Id);

/// <summary>Resposta de ingestão de lote aceita.</summary>
/// <param name="Ids">Identificadores dos registros aceitos, na ordem do lote.</param>
public sealed record LogEntryBatchAcceptedResponse(IReadOnlyList<Guid> Ids);

/// <summary>Payload de criação de um processo.</summary>
/// <param name="Name">Nome do processo. Obrigatório.</param>
/// <param name="ExternalReference">Referência de negócio do chamador (número do job, id do lote...).</param>
public sealed record CreateLogProcessRequest(string? Name, string? ExternalReference = null);

/// <summary>Payload de criação de um detail de processo (o processo vem da rota).</summary>
/// <param name="Level">Severidade do passo.</param>
/// <param name="Message">Mensagem. Obrigatória.</param>
/// <param name="StackTrace">Stack trace associado, quando houver.</param>
public sealed record CreateLogProcessDetailRequest(LogEntryLevel Level, string? Message, string? StackTrace = null);

/// <summary>Payload de registro de uma chamada de API externa.</summary>
/// <param name="Url">URL chamada (URI absoluto). Obrigatória.</param>
/// <param name="HttpMethod">Método HTTP. Obrigatório.</param>
/// <param name="IsSuccess">Sucesso segundo o chamador (falha de rede não tem status code).</param>
/// <param name="RequestBody">Corpo da requisição (opcional; truncado no limite configurado).</param>
/// <param name="RequestHeaders">Headers (opcional; valores sensíveis são redigidos no servidor — ADR-0020).</param>
/// <param name="ResponseStatusCode">Status HTTP da resposta, quando houve resposta.</param>
/// <param name="ResponseBody">Corpo da resposta (opcional; truncado no limite configurado).</param>
/// <param name="DurationMs">Duração da chamada em milissegundos.</param>
/// <param name="ErrorMessage">Mensagem de erro, quando houver.</param>
public sealed record CreateApiCallLogRequest(
    string? Url,
    string? HttpMethod,
    bool IsSuccess,
    string? RequestBody = null,
    Dictionary<string, string?>? RequestHeaders = null,
    int? ResponseStatusCode = null,
    string? ResponseBody = null,
    long? DurationMs = null,
    string? ErrorMessage = null);
