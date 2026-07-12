namespace Secco.LogStream.Domain.LogEntries;

/// <summary>
/// Nível de severidade de um registro de log. Enum próprio do domínio (ADR-0002: Domain
/// não referencia pacotes externos); numeração compatível com <c>Microsoft.Extensions.Logging.LogLevel</c>
/// para conversão trivial nos clients.
/// </summary>
public enum LogEntryLevel
{
    /// <summary>Detalhe fino de diagnóstico.</summary>
    Trace = 0,

    /// <summary>Informação de depuração.</summary>
    Debug = 1,

    /// <summary>Fluxo normal da aplicação.</summary>
    Information = 2,

    /// <summary>Situação anormal que não interrompe o fluxo.</summary>
    Warning = 3,

    /// <summary>Falha na operação atual.</summary>
    Error = 4,

    /// <summary>Falha que exige atenção imediata.</summary>
    Critical = 5,
}
