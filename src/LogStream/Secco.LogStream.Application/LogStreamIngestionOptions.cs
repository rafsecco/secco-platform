namespace Secco.LogStream.Application;

/// <summary>
/// Limites de ingestão (ADR-0020 — negação de serviço): valores default do perfil
/// balanceado, configuráveis pelo adotante na seção <c>LogStream:Ingestion</c>.
/// São limites de runtime — o schema não os fixa.
/// </summary>
public sealed class LogStreamIngestionOptions
{
    /// <summary>Tamanho máximo da mensagem, em caracteres (default 16 KB).</summary>
    public int MaxMessageLength { get; set; } = 16_384;

    /// <summary>Tamanho máximo do stack trace, em caracteres (default 128 KB).</summary>
    public int MaxStackTraceLength { get; set; } = 131_072;

    /// <summary>Quantidade máxima de itens por batch (default 500).</summary>
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>Capacidade da fila de ingestão em memória (default 10.000). Cheia → 503.</summary>
    public int QueueCapacity { get; set; } = 10_000;
}
