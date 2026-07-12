using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.LogStream.Infrastructure.Retention;

/// <summary>
/// Expurgo periódico de logs além da janela de retenção (ADR-0015 camada 1: manutenção
/// in-process; perder uma execução por restart é aceitável). Itera os bancos de tenant
/// via catálogo — cada tenant tem sua janela (<see cref="RetentionPolicy"/>). Postura
/// fail-safe: configuração ausente ou inválida = worker inativo, nada é apagado.
/// </summary>
internal sealed partial class LogRetentionWorker(
    LogStreamRetentionOptions options,
    LogStreamDatabaseOptions databaseOptions,
    ITenantCatalog tenantCatalog,
    ILogger<LogRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!RetentionPolicy.IsValid(options))
        {
            LogInvalidConfiguration(logger);
            return;
        }

        if (options.DefaultDays is null && options.DaysByTenant.Count == 0)
        {
            LogInactive(logger);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(options.IntervalHours));

        do
        {
            try
            {
                await PurgeAllTenantsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogRunFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task PurgeAllTenantsAsync(CancellationToken cancellationToken)
    {
        foreach (var tenant in await tenantCatalog.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            if (RetentionPolicy.ResolveDays(options, tenant.TenantId) is not { } days)
            {
                continue;
            }

            try
            {
                var cutoff = DateTimeOffset.UtcNow.AddDays(-days);
                var (entries, processes, apiCalls) = await PurgeTenantAsync(
                    databaseOptions.Provider, tenant.ConnectionString, cutoff, cancellationToken).ConfigureAwait(false);

                LogTenantPurged(logger, tenant.TenantId, days, entries, processes, apiCalls);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                LogTenantFailure(logger, exception, tenant.TenantId);
            }
        }
    }

    /// <summary>
    /// Expurga um banco de tenant: registros anteriores ao corte nas três tabelas —
    /// details de processos caem pelo cascade da FK no banco.
    /// </summary>
    internal static async Task<(int Entries, int Processes, int ApiCalls)> PurgeTenantAsync(
        LogStreamDatabaseProvider provider,
        string connectionString,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        var contextOptions = LogStreamDatabaseProviderConfigurator.CreateOptions(provider, connectionString);

        await using var context = new LogStreamDbContext(contextOptions);

        var entries = await context.LogEntries
            .Where(entry => entry.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var processes = await context.LogProcesses
            .Where(process => process.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        var apiCalls = await context.ApiCallLogs
            .Where(call => call.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

        return (entries, processes, apiCalls);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Retenção inativa: configuração 'LogStream:Retention' inválida — nada será expurgado (fail-safe).")]
    private static partial void LogInvalidConfiguration(ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information,
        Message = "Retenção inativa: sem 'LogStream:Retention:DefaultDays' configurado, nenhum log é expurgado (opt-in explícito).")]
    private static partial void LogInactive(ILogger logger);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information,
        Message = "Retenção do tenant {TenantId} ({Days} dias): {Entries} log(s), {Processes} processo(s), {ApiCalls} chamada(s) de API expurgados.")]
    private static partial void LogTenantPurged(ILogger logger, Guid tenantId, int days, int entries, int processes, int apiCalls);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error,
        Message = "Falha na retenção do tenant {TenantId} — os demais tenants seguem.")]
    private static partial void LogTenantFailure(ILogger logger, Exception exception, Guid tenantId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Error,
        Message = "Falha na execução da retenção; nova tentativa no próximo ciclo.")]
    private static partial void LogRunFailure(ILogger logger, Exception exception);
}
