using Secco.LogStream.Domain.LogEntries;

namespace Secco.LogStream.Domain.LogProcesses;

/// <summary>Status agregado de um processo, derivado do pior nível entre seus details.</summary>
public enum ProcessStatus
{
    /// <summary>Nenhum detail, ou todos até <see cref="LogEntryLevel.Information"/>.</summary>
    Success = 0,

    /// <summary>Ao menos um detail <see cref="LogEntryLevel.Warning"/>.</summary>
    Warning = 1,

    /// <summary>Ao menos um detail <see cref="LogEntryLevel.Error"/>.</summary>
    Error = 2,

    /// <summary>Ao menos um detail <see cref="LogEntryLevel.Critical"/>.</summary>
    Critical = 3,
}

/// <summary>
/// Regra de negócio do status agregado — função pura sobre o pior nível, computado
/// pela consulta (agregação SQL) ou por qualquer chamador.
/// </summary>
public static class ProcessStatusRule
{
    /// <summary>Deriva o status do pior nível entre os details (<c>null</c> = sem details).</summary>
    /// <param name="maxLevel">Maior severidade entre os details do processo, se houver algum.</param>
    public static ProcessStatus FromMaxLevel(LogEntryLevel? maxLevel) => maxLevel switch
    {
        null => ProcessStatus.Success,
        >= LogEntryLevel.Critical => ProcessStatus.Critical,
        LogEntryLevel.Error => ProcessStatus.Error,
        LogEntryLevel.Warning => ProcessStatus.Warning,
        _ => ProcessStatus.Success,
    };
}
