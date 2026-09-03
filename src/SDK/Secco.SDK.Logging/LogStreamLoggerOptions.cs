using Microsoft.Extensions.Logging;

namespace Secco.SDK.Logging;

/// <summary>Opções do sink <c>ILogger</c> → LogStream (seção <c>Secco:LogStream</c>).</summary>
public sealed class LogStreamLoggerOptions
{
	/// <summary>Chave da seção de configuração onde estas opções são lidas.</summary>
	public const string SectionKey = "Secco:LogStream";

	/// <summary>Liga ou desliga o sink por completo. Padrão: habilitado.</summary>
	public bool Enabled { get; set; } = true;

	/// <summary>URL base da API do Secco.LogStream.</summary>
	public string? BaseUrl { get; set; }

	/// <summary>Identificador do client OAuth usado no client credentials contra o SecureGate.</summary>
	public string? ClientId { get; set; }

	/// <summary>Segredo do client OAuth. Nunca é logado (ADR-0020).</summary>
	public string? ClientSecret { get; set; }

	/// <summary>Scope solicitado ao SecureGate; a audience correspondente é <c>secco-logstream</c>.</summary>
	public string Scope { get; set; } = "logstream";

	/// <summary>Nome do serviço enriquecido em cada entrada. Padrão: nome do assembly de entrada.</summary>
	public string? ServiceName { get; set; }

	/// <summary>
	/// Tenant de destino para logs emitidos fora de um escopo com tenant resolvido (startup,
	/// worker, job sem tenant). Nulo descarta essas entradas em vez de enviá-las.
	/// </summary>
	public Guid? PlatformTenantId { get; set; }

	/// <summary>Nível mínimo enviado ao LogStream. Padrão: <see cref="LogLevel.Information"/>.</summary>
	public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

	/// <summary>Capacidade máxima da fila local antes de começar a descartar entradas.</summary>
	public int QueueCapacity { get; set; } = 10_000;

	/// <summary>Quantidade máxima de entradas por lote enviado ao LogStream.</summary>
	public int BatchSize { get; set; } = 100;

	/// <summary>Intervalo, em milissegundos, entre envios de lote quando a fila não atinge <see cref="BatchSize"/>.</summary>
	public int FlushIntervalMs { get; set; } = 2_000;

	/// <summary>Tempo máximo, em milissegundos, que o encerramento do processo aguarda o flush final.</summary>
	public int ShutdownFlushTimeoutMs { get; set; } = 5_000;

	/// <summary>Tamanho máximo da mensagem antes de truncar.</summary>
	public int MaxMessageLength { get; set; } = 8_000;

	/// <summary>Tamanho máximo do stack trace antes de truncar.</summary>
	public int MaxStackTraceLength { get; set; } = 16_000;
}
