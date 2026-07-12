using Secco.SharedKernel.Results;

namespace Secco.LogStream.Application;

/// <summary>Erros de negócio do LogStream (ADR-0004): códigos estáveis <c>LogStream.*</c>.</summary>
public static class LogStreamErrors
{
    /// <summary>Erros de registros de log.</summary>
    public static class LogEntries
    {
        /// <summary>Mensagem ausente ou vazia.</summary>
        public static readonly Error MessageRequired =
            Error.Validation("LogStream.LogEntry.MessageRequired", "A mensagem do log é obrigatória.");

        /// <summary>Mensagem acima do limite configurado.</summary>
        public static Error MessageTooLong(int limit) =>
            Error.Validation("LogStream.LogEntry.MessageTooLong", $"A mensagem excede o limite de {limit} caracteres.");

        /// <summary>Stack trace acima do limite configurado.</summary>
        public static Error StackTraceTooLong(int limit) =>
            Error.Validation("LogStream.LogEntry.StackTraceTooLong", $"O stack trace excede o limite de {limit} caracteres.");

        /// <summary>Batch vazio.</summary>
        public static readonly Error BatchEmpty =
            Error.Validation("LogStream.LogEntry.BatchEmpty", "O batch deve conter ao menos um item.");

        /// <summary>Batch acima do limite configurado.</summary>
        public static Error BatchTooLarge(int limit) =>
            Error.Validation("LogStream.LogEntry.BatchTooLarge", $"O batch excede o limite de {limit} itens.");

        /// <summary>Registro não encontrado no banco do tenant atual.</summary>
        public static readonly Error NotFound =
            Error.NotFound("LogStream.LogEntry.NotFound", "Registro de log não encontrado.");

        /// <summary>Intervalo de busca invertido.</summary>
        public static readonly Error InvalidDateRange =
            Error.Validation("LogStream.LogEntry.InvalidDateRange", "A data inicial não pode ser posterior à final.");
    }

    /// <summary>Erros de processos.</summary>
    public static class LogProcesses
    {
        /// <summary>Nome do processo ausente ou vazio.</summary>
        public static readonly Error NameRequired =
            Error.Validation("LogStream.LogProcess.NameRequired", "O nome do processo é obrigatório.");

        /// <summary>Nome do processo acima do limite configurado.</summary>
        public static Error NameTooLong(int limit) =>
            Error.Validation("LogStream.LogProcess.NameTooLong", $"O nome do processo excede o limite de {limit} caracteres.");

        /// <summary>Referência externa acima do limite configurado.</summary>
        public static Error ExternalReferenceTooLong(int limit) =>
            Error.Validation("LogStream.LogProcess.ExternalReferenceTooLong", $"A referência externa excede o limite de {limit} caracteres.");

        /// <summary>Processo não encontrado no banco do tenant atual.</summary>
        public static readonly Error NotFound =
            Error.NotFound("LogStream.LogProcess.NotFound", "Processo não encontrado.");
    }

    /// <summary>Erros de chamadas de API.</summary>
    public static class ApiCalls
    {
        /// <summary>URL ausente ou vazia.</summary>
        public static readonly Error UrlRequired =
            Error.Validation("LogStream.ApiCallLog.UrlRequired", "A URL da chamada é obrigatória.");

        /// <summary>URL acima do limite configurado.</summary>
        public static Error UrlTooLong(int limit) =>
            Error.Validation("LogStream.ApiCallLog.UrlTooLong", $"A URL excede o limite de {limit} caracteres.");

        /// <summary>URL sem formato absoluto válido.</summary>
        public static readonly Error UrlMalformed =
            Error.Validation("LogStream.ApiCallLog.UrlMalformed", "A URL deve ser um URI absoluto válido.");

        /// <summary>Método HTTP fora do vocabulário conhecido.</summary>
        public static readonly Error MethodInvalid =
            Error.Validation("LogStream.ApiCallLog.MethodInvalid", "Método HTTP inválido.");

        /// <summary>Status code fora do intervalo HTTP.</summary>
        public static readonly Error StatusCodeOutOfRange =
            Error.Validation("LogStream.ApiCallLog.StatusCodeOutOfRange", "O status code deve estar entre 100 e 599.");

        /// <summary>Duração negativa.</summary>
        public static readonly Error DurationNegative =
            Error.Validation("LogStream.ApiCallLog.DurationNegative", "A duração não pode ser negativa.");

        /// <summary>Registro não encontrado no banco do tenant atual.</summary>
        public static readonly Error NotFound =
            Error.NotFound("LogStream.ApiCallLog.NotFound", "Registro de chamada de API não encontrado.");
    }

    /// <summary>Erros de ingestão.</summary>
    public static class Ingestion
    {
        /// <summary>Fila de ingestão na capacidade máxima — tente novamente (503 + Retry-After).</summary>
        public static readonly Error QueueFull =
            Error.Unavailable("LogStream.Ingestion.QueueFull", "A fila de ingestão está cheia; tente novamente em instantes.");

        /// <summary>Requisição sem tenant resolvido — logs sempre pertencem a um tenant (ADR-0005).</summary>
        public static readonly Error TenantNotResolved =
            Error.Validation("LogStream.Ingestion.TenantNotResolved", "Nenhum tenant foi resolvido para a requisição (claim tenant_id ou header X-Tenant-Id).");
    }
}
