namespace Secco.LogStream.Application;

/// <summary>
/// Permissões do LogStream (ADR-0021, formato canônico <c>recurso:acao</c> do kernel).
/// As constantes vivem no PRODUTO — não no SharedKernel — pela regra de admissão da
/// ADR-0003 (decisão da Fase 6.4). Usadas direto como policy:
/// <c>RequireAuthorization(LogStreamPermissions.LogEntries.Write)</c>.
/// </summary>
public static class LogStreamPermissions
{
    /// <summary>Permissões do log geral.</summary>
    public static class LogEntries
    {
        /// <summary>Consultar registros de log.</summary>
        public const string Read = "log-entries:read";

        /// <summary>Ingerir registros de log.</summary>
        public const string Write = "log-entries:write";
    }

    /// <summary>Permissões do log de processos.</summary>
    public static class LogProcesses
    {
        /// <summary>Consultar processos e detalhes.</summary>
        public const string Read = "log-processes:read";

        /// <summary>Ingerir processos e detalhes.</summary>
        public const string Write = "log-processes:write";
    }

    /// <summary>Permissões do log de chamadas de API.</summary>
    public static class ApiCallLogs
    {
        /// <summary>Consultar chamadas de API.</summary>
        public const string Read = "api-call-logs:read";

        /// <summary>Ingerir chamadas de API.</summary>
        public const string Write = "api-call-logs:write";
    }
}
