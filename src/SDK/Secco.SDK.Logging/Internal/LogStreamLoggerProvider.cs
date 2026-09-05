using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Secco.SDK.Logging.Internal;

/// <summary>
/// O <c>ILoggerProvider</c> que a ADR-0008 prometeu: liga o <c>ILogger&lt;T&gt;</c> que os
/// produtos já usam à ingestão do Secco.LogStream.
/// </summary>
/// <remarks>
/// A decisão de ignorar uma categoria é tomada <b>uma vez por categoria</b>, no
/// <see cref="CreateLogger"/>, e não a cada chamada de log: categoria ignorada recebe o
/// <see cref="NullLogger"/> do framework e o custo por chamada some.
/// </remarks>
/// <param name="queue">Fila local compartilhada.</param>
/// <param name="options">Opções do sink.</param>
internal sealed class LogStreamLoggerProvider(
	LogStreamLogQueue queue,
	IOptions<LogStreamLoggerOptions> options) : ILoggerProvider
{
	private readonly ConcurrentDictionary<string, ILogger> _loggers = new(StringComparer.Ordinal);

	private readonly LogStreamLoggerOptions _options = options.Value;

	private readonly string _serviceName = ResolveServiceName(options.Value);

	/// <inheritdoc />
	public ILogger CreateLogger(string categoryName) =>
		_loggers.GetOrAdd(
			categoryName ?? string.Empty,
			name => !_options.Enabled || LogStreamCategoryFilter.IsIgnored(name)
				? NullLogger.Instance
				: new LogStreamLogger(name, queue, _options, _serviceName));

	/// <inheritdoc />
	public void Dispose() => _loggers.Clear();

	/// <summary>Nome do serviço configurado ou, na ausência, o do assembly de entrada.</summary>
	/// <param name="options">Opções do sink.</param>
	private static string ResolveServiceName(LogStreamLoggerOptions options) =>
		string.IsNullOrWhiteSpace(options.ServiceName)
			? Assembly.GetEntryAssembly()?.GetName().Name ?? "desconhecido"
			: options.ServiceName.Trim();
}
