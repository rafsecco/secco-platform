using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
	IOptions<LogStreamRetentionOptions> retentionOptions,
	IOptions<LogStreamDatabaseOptions> databaseOptions,
	ITenantCatalog tenantCatalog,
	ILogger<LogRetentionWorker> logger) : BackgroundService
{
	private readonly LogStreamRetentionOptions _options = retentionOptions.Value;
	private readonly LogStreamDatabaseOptions _databaseOptions = databaseOptions.Value;

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!RetentionPolicy.IsValid(_options))
		{
			LogInvalidConfiguration(logger);
			return;
		}

		if (_options.DefaultDays is null && _options.DaysByTenant.Count == 0
			&& _options.AuditDefaultDays is null && _options.AuditDaysByTenant.Count == 0)
		{
			LogInactive(logger);
			return;
		}

		using var timer = new PeriodicTimer(TimeSpan.FromHours(_options.IntervalHours));

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
			var diagnosticDays = RetentionPolicy.ResolveDays(_options, tenant.TenantId);
			var auditDays = RetentionPolicy.ResolveAuditDays(_options, tenant.TenantId);

			if (diagnosticDays is null && auditDays is null)
			{
				continue;
			}

			try
			{
				var diagnosticCutoff = diagnosticDays is { } days ? DateTimeOffset.UtcNow.AddDays(-days) : (DateTimeOffset?)null;
				var auditCutoff = auditDays is { } aDays ? DateTimeOffset.UtcNow.AddDays(-aDays) : (DateTimeOffset?)null;

				var (entries, processes, apiCalls, auditEntries) = await PurgeTenantAsync(
					_databaseOptions.Provider, tenant.ConnectionString, diagnosticCutoff, auditCutoff, cancellationToken).ConfigureAwait(false);

				LogTenantPurged(logger, tenant.TenantId, diagnosticDays, entries, processes, apiCalls, auditDays, auditEntries);
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
	/// Expurga um banco de tenant: registros de diagnóstico (log geral, processos — details
	/// caem pelo cascade da FK — e chamadas de API) anteriores a <paramref name="diagnosticCutoff"/>,
	/// quando informado, e entradas de auditoria anteriores a <paramref name="auditCutoff"/>,
	/// quando informado. As duas janelas são independentes de propósito: a de diagnóstico
	/// NUNCA leva a trilha de auditoria junto — só o corte de auditoria expurga <c>tb_audit_entries</c>.
	/// </summary>
	internal static async Task<(int Entries, int Processes, int ApiCalls, int AuditEntries)> PurgeTenantAsync(
		LogStreamDatabaseProvider provider,
		string connectionString,
		DateTimeOffset? diagnosticCutoff,
		DateTimeOffset? auditCutoff = null,
		CancellationToken cancellationToken = default)
	{
		var contextOptions = LogStreamDatabaseProviderConfigurator.CreateOptions(provider, connectionString);

		await using var context = new LogStreamDbContext(contextOptions);

		var entries = 0;
		var processes = 0;
		var apiCalls = 0;
		var auditEntries = 0;

		if (diagnosticCutoff is { } cutoff)
		{
			entries = await context.LogEntries
				.Where(entry => entry.CreatedAt < cutoff)
				.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

			processes = await context.LogProcesses
				.Where(process => process.CreatedAt < cutoff)
				.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

			apiCalls = await context.ApiCallLogs
				.Where(call => call.CreatedAt < cutoff)
				.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		}

		if (auditCutoff is { } auditCutoffValue)
		{
			// O corte é pelo CreatedAt (carimbo do servidor), NUNCA pelo OccurredAt: este é
			// declarado pelo chamador, e retenção é operação destrutiva — deixar input externo
			// governá-la permitiria apagar uma trilha antes da hora com um OccurredAt forjado
			// no passado (ADR-0020). O OccurredAt serve para consultar o fato, não para expurgá-lo.
			auditEntries = await context.AuditEntries
				.Where(entry => entry.CreatedAt < auditCutoffValue)
				.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		}

		return (entries, processes, apiCalls, auditEntries);
	}

	[LoggerMessage(EventId = 1, Level = LogLevel.Warning,
		Message = "Retenção inativa: configuração 'LogStream:Retention' inválida — nada será expurgado (fail-safe).")]
	private static partial void LogInvalidConfiguration(ILogger logger);

	[LoggerMessage(EventId = 2, Level = LogLevel.Information,
		Message = "Retenção inativa: sem janela de diagnóstico nem de auditoria configurada em 'LogStream:Retention', nada é expurgado (opt-in explícito).")]
	private static partial void LogInactive(ILogger logger);

	[LoggerMessage(EventId = 3, Level = LogLevel.Information,
		Message = "Retenção do tenant {TenantId}: diagnóstico ({DiagnosticDays} dia(s), null = não configurada) expurgou {Entries} log(s), {Processes} processo(s) e {ApiCalls} chamada(s) de API; auditoria ({AuditDays} dia(s), null = não configurada) expurgou {AuditEntries} registro(s).")]
	private static partial void LogTenantPurged(
		ILogger logger, Guid tenantId, int? diagnosticDays, int entries, int processes, int apiCalls, int? auditDays, int auditEntries);

	[LoggerMessage(EventId = 4, Level = LogLevel.Error,
		Message = "Falha na retenção do tenant {TenantId} — os demais tenants seguem.")]
	private static partial void LogTenantFailure(ILogger logger, Exception exception, Guid tenantId);

	[LoggerMessage(EventId = 5, Level = LogLevel.Error,
		Message = "Falha na execução da retenção; nova tentativa no próximo ciclo.")]
	private static partial void LogRunFailure(ILogger logger, Exception exception);
}
