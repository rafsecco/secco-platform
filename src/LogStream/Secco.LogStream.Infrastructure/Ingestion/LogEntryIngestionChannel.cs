using System.Threading.Channels;
using Secco.LogStream.Application;
using Secco.LogStream.Domain.LogEntries;

namespace Secco.LogStream.Infrastructure.Ingestion;

/// <summary>Item da fila: o tenant viaja com o registro (ADR-0015) — o worker restaura o contexto.</summary>
/// <param name="TenantId">Tenant dono do registro.</param>
/// <param name="LogEntry">Registro a persistir.</param>
internal sealed record LogEntryWorkItem(Guid TenantId, LogEntry LogEntry);

/// <summary>
/// Canal <b>bounded</b> compartilhado entre a fila (escrita, escopo de request) e o worker
/// (leitura). Capacidade limitada é defesa de DoS/OOM (ADR-0020): cheio → <c>TryWrite</c>
/// falha e a API responde 503, nunca perde log silenciosamente.
/// </summary>
internal sealed class LogEntryIngestionChannel(LogStreamIngestionOptions options)
{
    private readonly Channel<LogEntryWorkItem> _channel = Channel.CreateBounded<LogEntryWorkItem>(
        new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ChannelWriter<LogEntryWorkItem> Writer => _channel.Writer;

    public ChannelReader<LogEntryWorkItem> Reader => _channel.Reader;
}
