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
