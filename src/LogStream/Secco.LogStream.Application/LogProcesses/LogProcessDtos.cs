using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;

namespace Secco.LogStream.Application.LogProcesses;

/// <summary>Representação de leitura de um processo, com o status agregado sempre presente.</summary>
/// <param name="Id">Identificador do processo.</param>
/// <param name="Name">Nome do processo.</param>
/// <param name="ExternalReference">Referência externa do chamador.</param>
/// <param name="CorrelationId">Correlation id de origem.</param>
/// <param name="CreatedAt">Momento da criação.</param>
/// <param name="Status">Status agregado (pior nível entre os details).</param>
/// <param name="DetailCount">Quantidade de details.</param>
public sealed record LogProcessDto(
    Guid Id,
    string Name,
    string? ExternalReference,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt,
    ProcessStatus Status,
    int DetailCount)
{
    /// <summary>Projeta o read model para o DTO, aplicando a regra de status do domínio.</summary>
    public static LogProcessDto FromSummary(LogProcessSummary summary) =>
        new(summary.Id,
            summary.Name,
            summary.ExternalReference,
            summary.CorrelationId,
            summary.CreatedAt,
            ProcessStatusRule.FromMaxLevel(summary.MaxDetailLevel),
            summary.DetailCount);
}

/// <summary>Representação de leitura de um detail de processo.</summary>
/// <param name="Id">Identificador do detail.</param>
/// <param name="LogProcessId">Processo pai.</param>
/// <param name="Level">Severidade.</param>
/// <param name="Message">Mensagem.</param>
/// <param name="StackTrace">Stack trace, quando houver.</param>
/// <param name="CorrelationId">Correlation id de origem.</param>
/// <param name="CreatedAt">Momento da criação.</param>
public sealed record LogProcessDetailDto(
    Guid Id,
    Guid LogProcessId,
    LogEntryLevel Level,
    string Message,
    string? StackTrace,
    Guid? CorrelationId,
    DateTimeOffset CreatedAt)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    public static LogProcessDetailDto FromEntity(LogProcessDetail entity) =>
        new(entity.Id, entity.LogProcessId, entity.Level, entity.Message,
            entity.StackTrace, entity.CorrelationId, entity.CreatedAt);
}
