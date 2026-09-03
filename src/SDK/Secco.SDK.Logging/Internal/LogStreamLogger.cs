using Microsoft.Extensions.Logging;
using Secco.SDK.AspNetCore.Ambient;

namespace Secco.SDK.Logging.Internal;

/// <summary>
/// O <c>ILogger</c> de uma categoria. Formata, enriquece com o contexto ambiente, trunca e
/// enfileira — sempre sem bloquear e sem nunca lançar.
/// </summary>
/// <remarks>
/// "Sem nunca lançar" é literal: uma falha aqui aconteceria dentro de um <c>catch</c> alheio,
/// no meio do tratamento de outro erro, e trocaria o problema original do produto por um
/// problema de logging. Toda a operação está sob um <c>catch</c> abrangente e silencioso.
/// </remarks>
/// <param name="category">Categoria do logger.</param>
/// <param name="queue">Fila local compartilhada.</param>
/// <param name="options">Opções vigentes do sink.</param>
/// <param name="serviceName">Nome do serviço, resolvido uma vez na composição.</param>
internal sealed class LogStreamLogger(
	string category,
	LogStreamLogQueue queue,
	LogStreamLoggerOptions options,
	string serviceName) : ILogger
{
	/// <summary>
	/// Escopos não são suportados: o <c>LogEntry</c> do LogStream não tem onde guardar dado
	/// estruturado de escopo, então prometer suporte seria enganar quem usa <c>BeginScope</c>.
	/// </summary>
	/// <typeparam name="TState">Tipo do estado do escopo.</typeparam>
	/// <param name="state">Estado do escopo, ignorado.</param>
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull => null;

	/// <inheritdoc />
	public bool IsEnabled(LogLevel logLevel) =>
		logLevel != LogLevel.None && logLevel >= options.MinimumLevel;

	/// <inheritdoc />
	public void Log<TState>(
		LogLevel logLevel,
		EventId eventId,
		TState state,
		Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		if (!IsEnabled(logLevel) || formatter is null)
		{
			return;
		}

		try
		{
			// Sem tenant não há banco de destino (ADR-0005, database-per-tenant). O tenant de
			// plataforma é o opt-in para não perder log de startup, worker e job sem tenant.
			if ((SeccoAmbientContext.TenantId ?? options.PlatformTenantId) is not { } tenantId)
			{
				return;
			}

			var message = Truncate(formatter(state, exception), options.MaxMessageLength);

			if (string.IsNullOrWhiteSpace(message))
			{
				// O LogStream exige mensagem não vazia; a da exceção é o melhor recurso restante.
				message = Truncate(exception?.Message, options.MaxMessageLength);

				if (string.IsNullOrWhiteSpace(message))
				{
					return;
				}
			}

			queue.Enqueue(new PendingLogEntry(
				tenantId,
				Guid.TryParse(SeccoAmbientContext.CorrelationId, out var correlationId) ? correlationId : null,
				logLevel,
				message,
				Truncate(exception?.ToString(), options.MaxStackTraceLength),
				serviceName,
				category));
		}
#pragma warning disable CA1031 // Falha ao logar jamais pode escapar para quem chamou o logger
		catch
		{
			// Silêncio proposital: não há para onde reportar sem arriscar recursão.
		}
#pragma warning restore CA1031
	}

	/// <summary>Corta o texto no limite configurado, sinalizando o corte.</summary>
	/// <param name="value">Texto original, possivelmente nulo.</param>
	/// <param name="maxLength">Limite máximo de caracteres.</param>
	private static string? Truncate(string? value, int maxLength) =>
		value is null || value.Length <= maxLength
			? value
			: string.Concat(value.AsSpan(0, maxLength), "… [truncado]");
}
