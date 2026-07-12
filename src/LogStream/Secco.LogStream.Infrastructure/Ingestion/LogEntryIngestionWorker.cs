using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.LogStream.Infrastructure.Ingestion;

/// <summary>
/// Consome a fila de ingestão e persiste cada item no banco do seu tenant
/// (escopo próprio por item + <c>SetTenant</c> — ADR-0015). Falha em um item é logada
/// e não interrompe o worker: indisponibilidade de banco não derruba a ingestão.
/// </summary>
internal sealed partial class LogEntryIngestionWorker(
    LogEntryIngestionChannel channel,
    IServiceProvider serviceProvider,
    ILogger<LogEntryIngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var workItem in channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                scope.ServiceProvider.SetTenant(workItem.TenantId);

                await workItem.PersistAsync(scope.ServiceProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogPersistenceFailure(logger, exception, workItem.GetType().Name, workItem.ItemId, workItem.TenantId);
            }
        }
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Error,
        Message = "Falha ao persistir {WorkItemType} {ItemId} do tenant {TenantId}.")]
    private static partial void LogPersistenceFailure(ILogger logger, Exception exception, string workItemType, Guid itemId, Guid tenantId);
}
