using Microsoft.Extensions.Logging;
using Secco.LogStream.Client;

namespace Secco.SDK.Logging.Internal;

/// <summary>Envio de um lote já agrupado por tenant. Único ponto que conhece o client gerado.</summary>
internal interface ILogStreamIngestionGateway
{
	/// <summary>Envia o lote de um tenant à ingestão do LogStream.</summary>
	/// <param name="tenantId">Tenant de destino — vira o header <c>X-Tenant-Id</c>.</param>
	/// <param name="entries">Entradas do lote, todas do mesmo tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task SendAsync(Guid tenantId, IReadOnlyList<PendingLogEntry> entries, CancellationToken cancellationToken);
}

/// <summary>
/// Implementação sobre o <c>ILogStreamClient</c> gerado por NSwag (ADR-0006).
/// </summary>
/// <remarks>
/// A abstração existe para confinar os tipos gerados a um arquivo só: o dispatcher, que é onde
/// mora a lógica de lote e de falha, fica testável sem depender do formato do client — e uma
/// regeneração do contrato não espalha mudança pelo pacote.
/// </remarks>
/// <param name="clientFactory">Fábrica do client já configurado com auth e header de tenant.</param>
internal sealed class LogStreamIngestionGateway(Func<ILogStreamClient> clientFactory) : ILogStreamIngestionGateway
{
	/// <inheritdoc />
	public async Task SendAsync(
		Guid tenantId,
		IReadOnlyList<PendingLogEntry> entries,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(entries);

		if (entries.Count == 0)
		{
			return;
		}

		// O escopo precisa envolver a chamada inteira: é dele que o handler HTTP lê o tenant.
		using var scope = LogStreamTenantScope.For(tenantId);

		await clientFactory()
			.BatchAsync(entries.Select(ToRequest).ToList(), cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>Converte uma entrada da fila no payload do contrato.</summary>
	/// <param name="entry">Entrada enfileirada.</param>
	private static CreateLogEntryRequest ToRequest(PendingLogEntry entry) =>
		new()
		{
			Level = ToLogEntryLevel(entry.Level),
			Message = entry.Message,
			StackTrace = entry.StackTrace,
			CorrelationId = entry.CorrelationId,
			ServiceName = entry.ServiceName,
			Category = entry.Category,
		};

	/// <summary>
	/// Converte a severidade do framework na do contrato. Explícito em vez de <c>cast</c>: a
	/// compatibilidade numérica entre os dois enums é documentada, não garantida por compilador,
	/// e um <c>cast</c> silenciaria a divergência se um dos lados mudar.
	/// </summary>
	/// <param name="level">Severidade do <c>ILogger</c>.</param>
	internal static LogEntryLevel ToLogEntryLevel(LogLevel level) => level switch
	{
		LogLevel.Trace => LogEntryLevel.Trace,
		LogLevel.Debug => LogEntryLevel.Debug,
		LogLevel.Information => LogEntryLevel.Information,
		LogLevel.Warning => LogEntryLevel.Warning,
		LogLevel.Error => LogEntryLevel.Error,
		LogLevel.Critical => LogEntryLevel.Critical,
		_ => LogEntryLevel.Information,
	};
}
