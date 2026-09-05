using Microsoft.Extensions.Logging;

namespace Secco.SDK.Logging.Internal;

/// <summary>
/// Uma entrada de log já enriquecida, esperando na fila local para ser enviada em lote.
/// </summary>
/// <remarks>
/// O tenant e a correlação são capturados no <b>momento do log</b>, não no momento do envio: o
/// dispatcher roda num <c>BackgroundService</c>, fora do escopo da requisição que originou a
/// entrada, e lá o contexto ambiente já não vale mais. Por isso o tenant aqui é
/// <see cref="Guid"/> e não <c>Guid?</c> — entrada sem tenant e sem tenant de plataforma
/// configurado é descartada antes de chegar à fila.
/// <para>
/// O nível viaja como <see cref="LogLevel"/> do framework, e não como o enum do contrato do
/// LogStream, para manter os tipos gerados pelo NSwag confinados ao gateway.
/// </para>
/// </remarks>
/// <param name="TenantId">Tenant de destino da entrada.</param>
/// <param name="CorrelationId">Correlação da requisição de origem, quando havia uma.</param>
/// <param name="Level">Severidade do registro.</param>
/// <param name="Message">Mensagem já formatada e truncada.</param>
/// <param name="StackTrace">Stack trace da exceção associada, já truncado.</param>
/// <param name="ServiceName">Serviço que emitiu o log.</param>
/// <param name="Category">Categoria do <c>ILogger</c> de origem.</param>
internal sealed record PendingLogEntry(
	Guid TenantId,
	Guid? CorrelationId,
	LogLevel Level,
	string Message,
	string? StackTrace,
	string ServiceName,
	string Category);
