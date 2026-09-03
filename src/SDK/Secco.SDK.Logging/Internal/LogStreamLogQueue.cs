using System.Threading.Channels;

namespace Secco.SDK.Logging.Internal;

/// <summary>
/// Fila local entre quem loga e quem envia. Limitada de propósito (ADR-0008: "LogStream
/// indisponível não pode derrubar produtos: fila local com descarte controlado").
/// </summary>
/// <remarks>
/// O modo de canal é <see cref="BoundedChannelFullMode.Wait"/> combinado com
/// <see cref="ChannelWriter{T}.TryWrite"/>, e a combinação é deliberada:
/// <see cref="BoundedChannelFullMode.DropWrite"/> descartaria silenciosamente devolvendo
/// <c>true</c>, e aí não haveria como contar o que se perdeu. Com <c>Wait</c>, o
/// <c>TryWrite</c> devolve <c>false</c> imediatamente quando a fila está cheia — sem bloquear
/// quem chamou <c>ILogger.Log</c> — e o descarte fica visível no contador.
/// </remarks>
internal sealed class LogStreamLogQueue
{
	private readonly Channel<PendingLogEntry> _channel;

	private long _droppedCount;

	/// <summary>Cria a fila com a capacidade configurada.</summary>
	/// <param name="capacity">Número máximo de entradas retidas antes do descarte.</param>
	public LogStreamLogQueue(int capacity)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

		_channel = Channel.CreateBounded<PendingLogEntry>(
			new BoundedChannelOptions(capacity)
			{
				FullMode = BoundedChannelFullMode.Wait,
				SingleReader = true,
				SingleWriter = false,
			});
	}

	/// <summary>Total de entradas descartadas por fila cheia desde o início do processo.</summary>
	public long DroppedCount => Interlocked.Read(ref _droppedCount);

	/// <summary>Leitor consumido pelo dispatcher.</summary>
	public ChannelReader<PendingLogEntry> Reader => _channel.Reader;

	/// <summary>
	/// Enfileira uma entrada sem nunca bloquear. Fila cheia (ou já encerrada) incrementa o
	/// contador de descarte e a entrada é perdida — de propósito.
	/// </summary>
	/// <param name="entry">Entrada já enriquecida e truncada.</param>
	public void Enqueue(PendingLogEntry entry)
	{
		if (!_channel.Writer.TryWrite(entry))
		{
			Interlocked.Increment(ref _droppedCount);
		}
	}

	/// <summary>Fecha a fila para escrita — o dispatcher ainda drena o que sobrou.</summary>
	public void Complete() => _channel.Writer.TryComplete();
}
