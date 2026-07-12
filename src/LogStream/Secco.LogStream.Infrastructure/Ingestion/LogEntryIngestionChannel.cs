using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Application;
using Secco.LogStream.Application.LogEntries;
using Secco.LogStream.Application.LogProcesses;
using Secco.LogStream.Domain.LogEntries;
using Secco.LogStream.Domain.LogProcesses;

namespace Secco.LogStream.Infrastructure.Ingestion;

/// <summary>
/// Item da fila: o tenant viaja com o registro (ADR-0015) e cada tipo sabe se persistir
/// no escopo do worker — a fila única FIFO garante ordem (processo antes dos seus details).
/// </summary>
/// <param name="TenantId">Tenant dono do registro.</param>
internal abstract record IngestionWorkItem(Guid TenantId)
{
    /// <summary>Identificador do registro, para diagnóstico de falhas.</summary>
    public abstract Guid ItemId { get; }

    /// <summary>Persiste o item usando os serviços do escopo do worker (tenant já estabelecido).</summary>
    public abstract Task PersistAsync(IServiceProvider scopedServices, CancellationToken cancellationToken);
}

/// <summary>Item de log geral.</summary>
internal sealed record LogEntryWorkItem(Guid TenantId, LogEntry LogEntry) : IngestionWorkItem(TenantId)
{
    public override Guid ItemId => LogEntry.Id;

    public override Task PersistAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        scopedServices.GetRequiredService<ILogEntryRepository>().AddAsync(LogEntry, cancellationToken);
}

/// <summary>Item de processo.</summary>
internal sealed record LogProcessWorkItem(Guid TenantId, LogProcess LogProcess) : IngestionWorkItem(TenantId)
{
    public override Guid ItemId => LogProcess.Id;

    public override Task PersistAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        scopedServices.GetRequiredService<ILogProcessRepository>().AddAsync(LogProcess, cancellationToken);
}

/// <summary>Item de detail de processo.</summary>
internal sealed record LogProcessDetailWorkItem(Guid TenantId, LogProcessDetail Detail) : IngestionWorkItem(TenantId)
{
    public override Guid ItemId => Detail.Id;

    public override Task PersistAsync(IServiceProvider scopedServices, CancellationToken cancellationToken) =>
        scopedServices.GetRequiredService<ILogProcessRepository>().AddDetailAsync(Detail, cancellationToken);
}

/// <summary>
/// Canal <b>bounded</b> compartilhado entre a fila (escrita, escopo de request) e o worker
/// (leitura). Capacidade limitada é defesa de DoS/OOM (ADR-0020): cheio → <c>TryWrite</c>
/// falha e a API responde 503, nunca perde log silenciosamente.
/// </summary>
internal sealed class LogEntryIngestionChannel(LogStreamIngestionOptions options)
{
    private readonly Channel<IngestionWorkItem> _channel = Channel.CreateBounded<IngestionWorkItem>(
        new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ChannelWriter<IngestionWorkItem> Writer => _channel.Writer;

    public ChannelReader<IngestionWorkItem> Reader => _channel.Reader;
}
