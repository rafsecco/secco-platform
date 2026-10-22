using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application.TenantAdministration;

namespace Secco.SecureGate.Infrastructure.TenantAdministration;

/// <summary>
/// Retenção opt-in da trilha de autoridade (ADR-0054, <c>SecureGate:Audit:RetentionDays</c>). Sem valor a trilha
/// não expira; valor fora de 1 a 3650 é ignorado e nada é apagado. Apaga em lotes, pelo instante do servidor, de
/// hora em hora. Idempotente — várias instâncias podem rodá-la —, e falha só registra: a próxima passada tenta de novo.
/// </summary>
internal sealed partial class AuthorityAuditRetentionService(
	IServiceScopeFactory scopeFactory,
	IOptions<AuthorityAuditOptions> options,
	TimeProvider clock,
	ILogger<AuthorityAuditRetentionService> logger) : BackgroundService
{
	/// <summary>Tamanho de cada lote de exclusão.</summary>
	public const int BatchSize = 1000;

	/// <summary>Intervalo entre passadas.</summary>
	public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

	/// <summary>Teto de lotes por passada, para uma passada nunca prender o banco indefinidamente.</summary>
	private const int MaxBatchesPerRun = 1000;

	/// <summary>Uma passada: apaga o vencido em lotes. Devolve quantas entradas saíram.</summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<int> RunOnceAsync(CancellationToken cancellationToken = default)
	{
		if (options.Value.EffectiveRetention is not { } retention)
		{
			return 0;
		}

		var cutoff = clock.GetUtcNow() - retention;
		var total = 0;

		try
		{
			for (var batch = 0; batch < MaxBatchesPerRun; batch++)
			{
				await using var scope = scopeFactory.CreateAsyncScope();
				var repository = scope.ServiceProvider.GetRequiredService<IAuthorityAuditRepository>();
				var deleted = await repository.DeleteOlderThanAsync(cutoff, BatchSize, cancellationToken).ConfigureAwait(false);
				total += deleted;

				if (deleted < BatchSize)
				{
					break;
				}
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
#pragma warning disable CA1031 // Falha de retenção só registra: a próxima passada tenta de novo
		catch (Exception exception)
#pragma warning restore CA1031
		{
			LogRetentionFailed(logger, exception.GetType().Name);
		}

		return total;
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		// Valor inválido: o aviso sai uma vez, na partida, e nada é apagado
		if (options.Value.IsRetentionInvalid)
		{
			LogInvalidRetention(logger, AuthorityAuditOptions.MinRetentionDays, AuthorityAuditOptions.MaxRetentionDays);

			return;
		}

		if (options.Value.EffectiveRetention is null)
		{
			return;
		}

		using var timer = new PeriodicTimer(Interval, clock);

		try
		{
			do
			{
				var deleted = await RunOnceAsync(stoppingToken).ConfigureAwait(false);

				if (deleted > 0)
				{
					LogRetentionDeleted(logger, deleted);
				}
			}
			while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
		}
		catch (OperationCanceledException)
		{
			// Parada normal do host
		}
	}

	[LoggerMessage(
		EventId = 3203,
		Level = LogLevel.Warning,
		Message = "SecureGate:Audit:RetentionDays fora de {Min} a {Max}: ignorado, a trilha de autoridade não será apagada.")]
	private static partial void LogInvalidRetention(ILogger logger, int min, int max);

	[LoggerMessage(
		EventId = 3204,
		Level = LogLevel.Warning,
		Message = "Retenção da trilha de autoridade falhou ({ExceptionType}); a próxima passada tenta de novo.")]
	private static partial void LogRetentionFailed(ILogger logger, string exceptionType);

	[LoggerMessage(EventId = 3205, Level = LogLevel.Information, Message = "Retenção da trilha de autoridade apagou {Count} entrada(s).")]
	private static partial void LogRetentionDeleted(ILogger logger, int count);
}
