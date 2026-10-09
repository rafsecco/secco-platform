using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// Memória, por processo, dos bancos de tenant já conferidos (ADR-0038). Chave = SHA-256 da
/// connection string — o texto nunca fica guardado aqui. Aberturas concorrentes do mesmo banco
/// aguardam a mesma tarefa; o lock do EF cobre as outras réplicas. Falha não é memorizada.
/// </summary>
public sealed class SeccoTenantMigrationGate
{
	private readonly ConcurrentDictionary<string, Lazy<Task>> _known = new(StringComparer.Ordinal);

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
		// CancellationToken.None na tarefa compartilhada: o cancelamento de UMA requisição não pode
		// cancelar a migração que outras estão aguardando.
		var entry = _known.GetOrAdd(key, _ => new Lazy<Task>(() => migrate(CancellationToken.None)));

		try
		{
			await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Qualquer falha de migração vira indisponibilidade transitória (503); a causa segue como InnerException
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_known.TryRemove(new KeyValuePair<string, Lazy<Task>>(key, entry));
			throw new TenantDatabaseUnavailableException(exception);
		}
#pragma warning restore CA1031
	}
}
