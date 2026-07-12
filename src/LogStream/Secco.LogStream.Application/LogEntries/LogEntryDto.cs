using Secco.LogStream.Domain.LogEntries;

namespace Secco.LogStream.Application.LogEntries;

/// <summary>Representação de leitura de um registro de log — a entidade nunca cruza a borda HTTP.</summary>
/// <param name="Id">Identificador do registro.</param>
/// <param name="Level">Severidade.</param>
/// <param name="Message">Mensagem.</param>
/// <param name="StackTrace">Stack trace, quando houver.</param>
/// <param name="CorrelationId">Correlation id de origem, quando propagado.</param>
/// <param name="CreatedAt">Momento da criação.</param>
public sealed record LogEntryDto(
    Guid Id,
    LogEntryLevel Level,
    string Message,
    string? StackTrace,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    public static LogEntryDto FromEntity(LogEntry entity) =>
        new(entity.Id, entity.Level, entity.Message, entity.StackTrace, entity.CorrelationId, entity.CreatedAt);
}
