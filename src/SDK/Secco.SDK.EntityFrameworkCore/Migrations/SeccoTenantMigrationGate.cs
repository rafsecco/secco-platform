using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// Memória, por processo, dos bancos de tenant já conferidos (ADR-0038). Chave = SHA-256 da
/// connection string — o texto nunca fica guardado aqui. Aberturas concorrentes do mesmo banco
/// aguardam a mesma tarefa; o lock do EF cobre as outras réplicas. A falha não torna o banco
/// "conferido", mas abre uma janela de backoff (<see cref="FailureBackoff"/>) em que a chave
/// responde indisponível direto — sem martelar um banco com problema disputando o lock do EF.
/// </summary>
public sealed class SeccoTenantMigrationGate(TimeProvider timeProvider)
{
	/// <summary>Janela após uma falha em que o banco responde indisponível sem nova tentativa (= Retry-After do SDK AspNetCore).</summary>
	public static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(15);

	private readonly ConcurrentDictionary<string, Lazy<Task>> _known = new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, (DateTimeOffset At, Exception Cause)> _failures = new(StringComparer.Ordinal);

	/// <summary>Gate com o relógio do sistema.</summary>
	public SeccoTenantMigrationGate()
		: this(TimeProvider.System)
	{
	}

	internal IReadOnlyCollection<string> Keys => [.. _known.Keys];

	/// <summary>Garante que o banco foi conferido/migrado neste processo.</summary>
	/// <param name="connectionString">Connection string do banco do tenant.</param>
	/// <param name="migrate">Aplica as migrations pendentes (sem o interceptor).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task EnsureMigratedAsync(string connectionString, Func<CancellationToken, Task> migrate, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(connectionString);
		ArgumentNullException.ThrowIfNull(migrate);

		var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)));
		if (_failures.TryGetValue(key, out var failure))
		{
			if (timeProvider.GetUtcNow() - failure.At < FailureBackoff)
			{
				throw new TenantDatabaseUnavailableException(failure.Cause);
			}

			_failures.TryRemove(new KeyValuePair<string, (DateTimeOffset At, Exception Cause)>(key, failure));
		}

		// CancellationToken.None na tarefa compartilhada: o cancelamento de UMA requisição não pode
		// cancelar a migração que outras estão aguardando.
		var entry = _known.GetOrAdd(key, _ => new Lazy<Task>(() => migrate(CancellationToken.None)));

		try
		{
			await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
			_failures.TryRemove(key, out _);
		}
#pragma warning disable CA1031 // Qualquer falha de migração vira indisponibilidade transitória (503); a causa segue como InnerException
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_known.TryRemove(new KeyValuePair<string, Lazy<Task>>(key, entry));
			_failures[key] = (timeProvider.GetUtcNow(), exception);
			throw new TenantDatabaseUnavailableException(exception);
		}
#pragma warning restore CA1031
	}
}
