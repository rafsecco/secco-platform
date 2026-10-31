using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application.Federation;
using Secco.SecureGate.Application.Scheduling;

namespace Secco.SecureGate.Infrastructure.Federation;

/// <summary>Resultado de uma passada do ciclo de sincronização com o diretório.</summary>
/// <param name="Ran">A passada rodou (o lease foi tomado e o ciclo percorreu os tenants).</param>
/// <param name="Tenants">Tenants percorridos.</param>
/// <param name="Failed">Tenants em que a reconciliação lançou exceção (isolados: os demais seguiram).</param>
internal sealed record DirectorySyncRun(bool Ran, int Tenants = 0, int Failed = 0)
{
	/// <summary>Passada que não rodou: sem app registration, ou o lease está com outra instância (ou ainda válido).</summary>
	public static readonly DirectorySyncRun NotRun = new(false);
}

/// <summary>
/// Ciclo periódico de reconciliação de perfis com o diretório federado (ADR-0055, entrega A). Roda em cada
/// instância do SecureGate, mas só uma executa por vez: a execução exige o lease no banco de plataforma
/// (<see cref="IBackgroundLeaseStore"/>), que o ciclo deixa válido até a próxima execução devida — então o ciclo roda
/// uma vez por intervalo, e não uma vez por instância (ADR-0035). Se a instância cai no meio, o lease curto expira e
/// outra assume na passada seguinte.
/// </summary>
/// <remarks>
/// Cada tenant é reconciliado no próprio escopo de DI e isolado dos demais: falha num não para os outros. Sem a app
/// registration do Entra configurada (<c>SecureGate:EntraId</c>) não há como ler o diretório, e o ciclo não faz nada.
/// Idempotente: a reconciliação só mexe no que difere do desejado.
/// </remarks>
internal sealed partial class DirectorySyncService(
	IServiceScopeFactory scopeFactory,
	IOptions<DirectorySyncOptions> options,
	IOptions<SecureGateEntraIdOptions> entraOptions,
	TimeProvider clock,
	ILogger<DirectorySyncService> logger) : BackgroundService
{
	/// <summary>Nome da tarefa no lease.</summary>
	public const string TaskName = "directory-sync";

	/// <summary>Quanto o lease vale enquanto o ciclo roda; renovado a cada tenant.</summary>
	public static readonly TimeSpan RunLeaseDuration = TimeSpan.FromMinutes(30);

	/// <summary>Frequência com que cada instância confere se o ciclo está devido.</summary>
	public static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

	/// <summary>Espera inicial, para a aplicação terminar de subir (migrations, seed) antes do primeiro ciclo.</summary>
	public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

	// Identifica a instância no lease (máx. 100 caracteres): nome da máquina, só para leitura humana, mais um sufixo único
	private readonly string _owner =
		$"{(Environment.MachineName.Length > 60 ? Environment.MachineName[..60] : Environment.MachineName)}:{Guid.NewGuid():N}";

	/// <summary>Uma passada: toma o lease, percorre os tenants elegíveis e deixa o lease válido até a próxima execução.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<DirectorySyncRun> RunOnceAsync(CancellationToken cancellationToken = default)
	{
		if (!entraOptions.Value.IsConfigured)
		{
			return DirectorySyncRun.NotRun;
		}

		var interval = options.Value.EffectiveInterval;
		var startedAt = clock.GetUtcNow();

		await using var leaseScope = scopeFactory.CreateAsyncScope();
		var lease = leaseScope.ServiceProvider.GetRequiredService<IBackgroundLeaseStore>();

		if (!await lease.TryAcquireAsync(TaskName, _owner, RunLeaseDuration, startedAt, cancellationToken).ConfigureAwait(false))
		{
			return DirectorySyncRun.NotRun;
		}

		var tenantsSeen = 0;
		var failed = 0;
		var nextDueAt = startedAt + interval;

		try
		{
			IReadOnlyList<DirectorySyncTenant> tenants;

			await using (var listScope = scopeFactory.CreateAsyncScope())
			{
				tenants = await listScope.ServiceProvider.GetRequiredService<IDirectorySyncTenantSource>()
					.ListEligibleAsync(cancellationToken).ConfigureAwait(false);
			}

			foreach (var tenant in tenants)
			{
				cancellationToken.ThrowIfCancellationRequested();

				// Lease perdido para outra instância (ex.: esta ficou parada além da duração): para, sem pisar
				if (!await lease.RenewAsync(TaskName, _owner, RunLeaseDuration, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false))
				{
					LogLeaseLost(logger);
					nextDueAt = startedAt;

					break;
				}

				tenantsSeen++;

				if (!await ReconcileTenantAsync(tenant, cancellationToken).ConfigureAwait(false))
				{
					failed++;
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Parada do host: devolve o lease para a próxima instância não esperar a expiração
			await lease.CompleteAsync(TaskName, _owner, startedAt, CancellationToken.None).ConfigureAwait(false);

			throw;
		}
#pragma warning disable CA1031 // Falha ao listar os tenants: libera o lease para a próxima passada tentar de novo
		catch (Exception exception)
#pragma warning restore CA1031
		{
			LogCycleFailed(logger, exception.GetType().Name);
			nextDueAt = startedAt;
		}

		await lease.CompleteAsync(TaskName, _owner, nextDueAt, cancellationToken).ConfigureAwait(false);

		return new DirectorySyncRun(true, tenantsSeen, failed);
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		WarnAboutInvalidOptions();

		using var timer = new PeriodicTimer(PollInterval, clock);

		try
		{
			await Task.Delay(StartupDelay, clock, stoppingToken).ConfigureAwait(false);

			do
			{
				try
				{
					var run = await RunOnceAsync(stoppingToken).ConfigureAwait(false);

					if (run.Ran)
					{
						LogCycleDone(logger, run.Tenants, run.Failed);
					}
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
					throw;
				}
#pragma warning disable CA1031 // O ciclo nunca derruba o host: a próxima passada tenta de novo
				catch (Exception exception)
#pragma warning restore CA1031
				{
					LogCycleFailed(logger, exception.GetType().Name);
				}
			}
			while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
		}
		catch (OperationCanceledException)
		{
			// Parada normal do host
		}
	}

	private async Task<bool> ReconcileTenantAsync(DirectorySyncTenant tenant, CancellationToken cancellationToken)
	{
		try
		{
			// Escopo de DI próprio por tenant: contexto, escopo de acesso e cache de EF não vazam entre tenants
			await using var scope = scopeFactory.CreateAsyncScope();
			var reconciler = scope.ServiceProvider.GetRequiredService<DirectoryRoleReconciler>();
			var result = await reconciler.ReconcileTenantAsync(tenant.TenantId, tenant.DirectoryId, cancellationToken)
				.ConfigureAwait(false);

			switch (result.Outcome)
			{
				case DirectorySyncOutcome.ReadFailed:
					LogTenantReadFailed(logger, tenant.TenantId, result.ErrorCode ?? "?");
					break;
				case DirectorySyncOutcome.AbortedByBrake:
					LogTenantAborted(logger, tenant.TenantId);
					break;
				case DirectorySyncOutcome.Completed when result.Added + result.Removed + result.Blocked + result.Unrecorded > 0:
					LogTenantReconciled(logger, tenant.TenantId, result.Added, result.Removed, result.Blocked, result.Unrecorded);
					break;
			}

			return true;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
#pragma warning disable CA1031 // Isolamento por tenant: falha num não para os outros
		catch (Exception exception)
#pragma warning restore CA1031
		{
			// Só o TIPO da exceção (ADR-0020): a mensagem pode trazer trecho de SQL ou de resposta do Graph
			LogTenantFailed(logger, tenant.TenantId, exception.GetType().Name);

			return false;
		}
	}

	private void WarnAboutInvalidOptions()
	{
		var current = options.Value;

		if (current.IsIntervalInvalid)
		{
			LogInvalidInterval(
				logger, DirectorySyncOptions.MinIntervalHours, DirectorySyncOptions.MaxIntervalHours, DirectorySyncOptions.DefaultIntervalHours);
		}

		if (current.IsRatioInvalid)
		{
			LogInvalidRatio(logger, DirectorySyncOptions.DefaultMaxRemovalRatio);
		}

		if (current.IsMinRemovalsInvalid)
		{
			LogInvalidMinRemovals(logger, DirectorySyncOptions.DefaultMinRemovalsBeforeBrake);
		}
	}

	[LoggerMessage(
		EventId = 3501,
		Level = LogLevel.Warning,
		Message = "SecureGate:DirectorySync:IntervalHours fora de {Min} a {Max}: usando o padrão de {Default} h.")]
	private static partial void LogInvalidInterval(ILogger logger, int min, int max, int @default);

	[LoggerMessage(
		EventId = 3502,
		Level = LogLevel.Warning,
		Message = "SecureGate:DirectorySync:MaxRemovalRatio fora de 0 a 1: usando o padrão de {Default}.")]
	private static partial void LogInvalidRatio(ILogger logger, double @default);

	[LoggerMessage(
		EventId = 3503,
		Level = LogLevel.Warning,
		Message = "SecureGate:DirectorySync:MinRemovalsBeforeBrake negativo: usando o padrão de {Default}.")]
	private static partial void LogInvalidMinRemovals(ILogger logger, int @default);

	[LoggerMessage(
		EventId = 3504,
		Level = LogLevel.Information,
		Message = "Sincronização com o diretório: {Tenants} tenant(s) percorrido(s), {Failed} com falha.")]
	private static partial void LogCycleDone(ILogger logger, int tenants, int failed);

	[LoggerMessage(
		EventId = 3505,
		Level = LogLevel.Warning,
		Message = "Sincronização com o diretório falhou ({ExceptionType}); a próxima passada tenta de novo.")]
	private static partial void LogCycleFailed(ILogger logger, string exceptionType);

	[LoggerMessage(
		EventId = 3506,
		Level = LogLevel.Warning,
		Message = "Sincronização com o diretório: lease perdido para outra instância; esta passada para aqui.")]
	private static partial void LogLeaseLost(ILogger logger);

	[LoggerMessage(
		EventId = 3507,
		Level = LogLevel.Warning,
		Message = "Sincronização do tenant {TenantId}: leitura do diretório falhou ({ErrorCode}); nada mudou.")]
	private static partial void LogTenantReadFailed(ILogger logger, Guid tenantId, string errorCode);

	[LoggerMessage(
		EventId = 3508,
		Level = LogLevel.Warning,
		Message = "Sincronização do tenant {TenantId} abortada pelo freio de massa; nada mudou.")]
	private static partial void LogTenantAborted(ILogger logger, Guid tenantId);

	[LoggerMessage(
		EventId = 3509,
		Level = LogLevel.Information,
		Message = "Sincronização do tenant {TenantId}: {Added} acrescentado(s), {Removed} retirado(s), {Blocked} barrado(s), {Unrecorded} sem trilha.")]
	private static partial void LogTenantReconciled(ILogger logger, Guid tenantId, int added, int removed, int blocked, int unrecorded);

	[LoggerMessage(
		EventId = 3510,
		Level = LogLevel.Warning,
		Message = "Sincronização do tenant {TenantId} falhou ({ExceptionType}); os demais tenants seguem.")]
	private static partial void LogTenantFailed(ILogger logger, Guid tenantId, string exceptionType);
}
