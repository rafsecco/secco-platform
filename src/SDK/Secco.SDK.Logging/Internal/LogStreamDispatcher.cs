using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Secco.SDK.Logging.Internal;

/// <summary>
/// Drena a fila local e entrega os lotes ao LogStream (ADR-0015 camada 1: manutenção in-process,
/// perder uma execução por restart é aceitável para log de diagnóstico).
/// </summary>
/// <remarks>
/// Duas regras governam este tipo. A primeira: falha de envio <b>nunca</b> escapa — o lote é
/// descartado, o fato é reportado nos providers locais e o ciclo segue. A segunda: cada lote
/// contém um único tenant, porque o destino é o banco daquele tenant (ADR-0005) e uma entrada
/// jamais pode viajar no lote de outro.
/// <para>
/// O logger deste tipo cai na lista de categorias ignoradas do
/// <see cref="LogStreamCategoryFilter"/>, então o que ele reporta vai para console e arquivo e
/// nunca realimenta a fila.
/// </para>
/// </remarks>
/// <param name="queue">Fila local compartilhada.</param>
/// <param name="gateway">Transporte até a ingestão do LogStream.</param>
/// <param name="options">Opções do sink.</param>
/// <param name="logger">Logger local para descartes e falhas.</param>
internal sealed partial class LogStreamDispatcher(
	LogStreamLogQueue queue,
	ILogStreamIngestionGateway gateway,
	IOptions<LogStreamLoggerOptions> options,
	ILogger<LogStreamDispatcher> logger) : BackgroundService
{
	private readonly LogStreamLoggerOptions _options = options.Value;

	private long _reportedDrops;

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		if (!_options.Enabled)
		{
			return;
		}

		var buffer = new List<PendingLogEntry>(_options.BatchSize);

		while (!stoppingToken.IsCancellationRequested)
		{
			try
			{
				if (!await queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
				{
					// Fila encerrada: quem drena o resto é o StopAsync.
					return;
				}

				await FillBatchAsync(buffer, stoppingToken).ConfigureAwait(false);
				await SendBatchAsync(buffer, stoppingToken).ConfigureAwait(false);
				ReportDrops();
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
				// O buffer pode ter entradas já retiradas da fila quando o shutdown chegou.
				// Sem esta tentativa elas se perderiam: o StopAsync drena a FILA, e o que está
				// aqui dentro não está mais lá.
				await FlushOnShutdownAsync(buffer).ConfigureAwait(false);

				return;
			}
#pragma warning disable CA1031 // O laço de envio não pode morrer por causa de um ciclo ruim
			catch (Exception exception)
			{
				LogCycleFailure(logger, exception);
			}
#pragma warning restore CA1031
			finally
			{
				buffer.Clear();
			}
		}
	}

	/// <summary>
	/// Encerramento: fecha a fila, drena o que restou dentro do prazo e só então para.
	/// </summary>
	/// <remarks>
	/// A ordem importa. <c>base.StopAsync</c> vem primeiro para parar o laço principal — dois
	/// leitores no mesmo canal disputariam as mesmas entradas. Só depois a fila é fechada e o
	/// resto é drenado, com prazo próprio: um LogStream lento não pode segurar o shutdown do
	/// produto indefinidamente.
	/// </remarks>
	/// <param name="cancellationToken">Token de parada do host.</param>
	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		await base.StopAsync(cancellationToken).ConfigureAwait(false);

		if (!_options.Enabled)
		{
			return;
		}

		queue.Complete();

		using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.ShutdownFlushTimeoutMs));

		try
		{
			await DrainRemainingAsync(timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			LogShutdownFlushTimeout(logger, _options.ShutdownFlushTimeoutMs);
		}
#pragma warning disable CA1031 // Nada no encerramento justifica derrubar o shutdown do produto
		catch (Exception exception)
		{
			LogCycleFailure(logger, exception);
		}
#pragma warning restore CA1031

		ReportDrops();
	}

	/// <summary>
	/// Junta entradas até completar o lote ou até a janela de flush fechar — o que vier antes.
	/// </summary>
	/// <param name="buffer">Acumulador do lote em formação.</param>
	/// <param name="stoppingToken">Token de parada do host.</param>
	private async Task FillBatchAsync(List<PendingLogEntry> buffer, CancellationToken stoppingToken)
	{
		using var window = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
		window.CancelAfter(TimeSpan.FromMilliseconds(_options.FlushIntervalMs));

		try
		{
			while (buffer.Count < _options.BatchSize)
			{
				if (queue.Reader.TryRead(out var entry))
				{
					buffer.Add(entry);
					continue;
				}

				if (!await queue.Reader.WaitToReadAsync(window.Token).ConfigureAwait(false))
				{
					return;
				}
			}
		}
		catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
		{
			// A janela fechou: envia o que já se juntou. É o caminho normal em volume baixo.
		}
	}

	/// <summary>Envia o buffer, um lote por tenant; falha de um tenant não afeta os demais.</summary>
	/// <param name="buffer">Entradas acumuladas, possivelmente de vários tenants.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	private async Task SendBatchAsync(List<PendingLogEntry> buffer, CancellationToken cancellationToken)
	{
		foreach (var group in buffer.GroupBy(entry => entry.TenantId))
		{
			var entries = group.ToList();

			try
			{
				await gateway.SendAsync(group.Key, entries, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
#pragma warning disable CA1031 // ADR-0008: LogStream indisponível não derruba o produto
			catch (Exception exception)
			{
				// O lote se perde de propósito. Repetir aqui só transferiria a pressão para a
				// memória do processo; a resiliência do SDK já tentou no nível do HttpClient.
				LogBatchDiscarded(logger, exception, group.Key, entries.Count);
			}
#pragma warning restore CA1031
		}
	}

	/// <summary>Drena tudo o que restou na fila fechada, em lotes do tamanho configurado.</summary>
	/// <param name="cancellationToken">Prazo do flush de encerramento.</param>
	private async Task DrainRemainingAsync(CancellationToken cancellationToken)
	{
		var buffer = new List<PendingLogEntry>(_options.BatchSize);

		while (queue.Reader.TryRead(out var entry))
		{
			buffer.Add(entry);

			if (buffer.Count < _options.BatchSize)
			{
				continue;
			}

			await SendBatchAsync(buffer, cancellationToken).ConfigureAwait(false);
			buffer.Clear();
		}

		if (buffer.Count > 0)
		{
			await SendBatchAsync(buffer, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Última tentativa de enviar o buffer em voo quando o host manda parar, com prazo próprio —
	/// o token do host já está cancelado e não serviria para nada.
	/// </summary>
	/// <param name="buffer">Entradas retiradas da fila e ainda não enviadas.</param>
	private async Task FlushOnShutdownAsync(List<PendingLogEntry> buffer)
	{
		if (buffer.Count == 0)
		{
			return;
		}

		using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.ShutdownFlushTimeoutMs));

		try
		{
			await SendBatchAsync(buffer, timeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			LogShutdownFlushTimeout(logger, _options.ShutdownFlushTimeoutMs);
		}
#pragma warning disable CA1031 // Nada no encerramento justifica derrubar o shutdown do produto
		catch (Exception exception)
		{
			LogCycleFailure(logger, exception);
		}
#pragma warning restore CA1031
	}

	/// <summary>Reporta, uma vez por descarte novo, quantas entradas a fila cheia já perdeu.</summary>
	private void ReportDrops()
	{
		var dropped = queue.DroppedCount;

		if (dropped > _reportedDrops)
		{
			LogEntriesDropped(logger, dropped - _reportedDrops, dropped);
			_reportedDrops = dropped;
		}
	}

	[LoggerMessage(EventId = 1, Level = LogLevel.Error,
		Message = "Lote de {Count} log(s) do tenant {TenantId} descartado: o LogStream não aceitou o envio.")]
	private static partial void LogBatchDiscarded(ILogger logger, Exception exception, Guid tenantId, int count);

	[LoggerMessage(EventId = 2, Level = LogLevel.Warning,
		Message = "Fila local do LogStream cheia: {Count} log(s) descartado(s) neste ciclo, {Total} no total do processo.")]
	private static partial void LogEntriesDropped(ILogger logger, long count, long total);

	[LoggerMessage(EventId = 3, Level = LogLevel.Error,
		Message = "Falha no ciclo de envio de logs; o próximo ciclo segue normalmente.")]
	private static partial void LogCycleFailure(ILogger logger, Exception exception);

	[LoggerMessage(EventId = 4, Level = LogLevel.Warning,
		Message = "Flush final de logs excedeu {TimeoutMs} ms no encerramento; o que restou na fila foi perdido.")]
	private static partial void LogShutdownFlushTimeout(ILogger logger, int timeoutMs);
}
