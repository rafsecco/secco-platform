using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SharedKernel.Constants;

namespace Secco.SDK.AspNetCore.Authentication;

/// <summary>
/// Confere a <c>sver</c> de um principal contra a versão atual (ADR-0032): cache por usuário com TTL curto e
/// fail-closed — mesma postura do cache de permissões da ADR-0021.
/// </summary>
public sealed partial class SessionVersionChecker(
	IServiceProvider serviceProvider,
	IOptions<SeccoSessionVersionOptions> options,
	ILogger<SessionVersionChecker> logger)
{
	private sealed record Entry(SessionVersionStatus Status, DateTimeOffset FreshUntil);

	private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

	/// <summary>Indica se a sessão do principal ainda vale.</summary>
	/// <param name="principal">Principal autenticado (token ou cookie).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async ValueTask<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(principal);

		// Sem sver: emitido antes da ADR-0032, ou token de máquina. A ausência não é forjável (assinatura).
		if (principal.FindFirst(SeccoClaims.SessionVersion)?.Value is not { Length: > 0 } presented)
		{
			return true;
		}

		var resolver = serviceProvider.GetService<ISessionVersionResolver>();

		if (resolver is not { IsEnabled: true })
		{
			return true;
		}

		if (principal.FindFirst(SeccoClaims.Subject)?.Value is not { Length: > 0 } subject)
		{
			return false;
		}

		var now = DateTimeOffset.UtcNow;

		if (!_entries.TryGetValue(subject, out var entry) || now >= entry.FreshUntil)
		{
			try
			{
				var status = await resolver.ResolveAsync(subject, cancellationToken).ConfigureAwait(false);
				var ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.SessionVersionCacheTtlSeconds));
				entry = new Entry(status, now + ttl);
				_entries[subject] = entry;
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				// Fail-closed: sem confirmar a versão, a sessão não é aceita. Nunca loga o token.
				LogResolutionFailed(logger, exception);
				return false;
			}
		}

		return !entry.Status.Revoked && string.Equals(entry.Status.SessionVersion, presented, StringComparison.Ordinal);
	}

	[LoggerMessage(EventId = 1301, Level = LogLevel.Warning,
		Message = "Versão de sessão indisponível; requisição recusada (fail-closed).")]
	private static partial void LogResolutionFailed(ILogger logger, Exception exception);
}
