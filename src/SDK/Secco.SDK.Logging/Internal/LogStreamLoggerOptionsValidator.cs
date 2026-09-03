using Microsoft.Extensions.Options;

namespace Secco.SDK.Logging.Internal;

/// <summary>
/// Validação das opções do sink, executada no startup (<c>ValidateOnStart</c>).
/// </summary>
/// <remarks>
/// Fail-fast por decisão (ADR-0020): configuração parcial nunca degrada em silêncio. Ou o sink
/// está desligado explicitamente, ou tem tudo o que precisa para funcionar — um produto que
/// sobe achando que loga, e não loga, é pior que um produto que não sobe.
/// </remarks>
internal sealed class LogStreamLoggerOptionsValidator : IValidateOptions<LogStreamLoggerOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, LogStreamLoggerOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (!options.Enabled)
		{
			return ValidateOptionsResult.Success;
		}

		var failures = new List<string>();

		ValidateAbsoluteUrl(options.BaseUrl, nameof(LogStreamLoggerOptions.BaseUrl), failures);
		ValidateAbsoluteUrl(options.AuthorityUrl, nameof(LogStreamLoggerOptions.AuthorityUrl), failures);
		ValidateRequired(options.ClientId, nameof(LogStreamLoggerOptions.ClientId), failures);
		ValidateRequired(options.ClientSecret, nameof(LogStreamLoggerOptions.ClientSecret), failures);
		ValidateRequired(options.Scope, nameof(LogStreamLoggerOptions.Scope), failures);

		ValidatePositive(options.QueueCapacity, nameof(LogStreamLoggerOptions.QueueCapacity), failures);
		ValidatePositive(options.BatchSize, nameof(LogStreamLoggerOptions.BatchSize), failures);
		ValidatePositive(options.FlushIntervalMs, nameof(LogStreamLoggerOptions.FlushIntervalMs), failures);
		ValidatePositive(options.ShutdownFlushTimeoutMs, nameof(LogStreamLoggerOptions.ShutdownFlushTimeoutMs), failures);
		ValidatePositive(options.MaxMessageLength, nameof(LogStreamLoggerOptions.MaxMessageLength), failures);
		ValidatePositive(options.MaxStackTraceLength, nameof(LogStreamLoggerOptions.MaxStackTraceLength), failures);

		return failures.Count == 0
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(
				$"Configuração inválida do sink de log (seção '{LogStreamLoggerOptions.SectionKey}'): " +
				$"{string.Join("; ", failures)}. Para desligar o envio ao LogStream, defina " +
				$"'{LogStreamLoggerOptions.SectionKey}:Enabled' como false.");
	}

	private static void ValidateRequired(string? value, string key, List<string> failures)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			failures.Add($"'{key}' é obrigatória");
		}
	}

	private static void ValidateAbsoluteUrl(string? value, string key, List<string> failures)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			failures.Add($"'{key}' é obrigatória");
			return;
		}

		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
			|| (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
		{
			failures.Add($"'{key}' deve ser uma URL http(s) absoluta");
		}
	}

	private static void ValidatePositive(int value, string key, List<string> failures)
	{
		if (value <= 0)
		{
			failures.Add($"'{key}' deve ser maior que zero");
		}
	}
}
