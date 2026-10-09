using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// O "processo controlado" da ADR-0005, materializado pela ADR-0038: migrations de todos os
/// <see cref="ISeccoDatabaseMigrator"/> e, só se TODAS passarem, o seed de referência. Chamado
/// pelo verbo <c>migrate</c> e pelo startup em Development — o mesmo código nos dois caminhos.
/// </summary>
public static class SeccoMigrationExtensions
{
	/// <summary>Executa migrations e seed. Devolve <c>true</c> em sucesso total.</summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<bool> RunSeccoMigrationsAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SeccoMigrationExtensions))
			?? NullLogger.Instance;
		var failed = false;

		await using (var scope = serviceProvider.CreateAsyncScope())
		{
			foreach (var migrator in scope.ServiceProvider.GetServices<ISeccoDatabaseMigrator>())
			{
				try
				{
					var failures = await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);

					foreach (var target in failures)
					{
						MigrationLog.TargetFailed(logger, migrator.Name, target);
					}

					failed |= failures.Count > 0;
				}
#pragma warning disable CA1031 // Falha de um migrator não pode derrubar os demais; logada e contada
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					MigrationLog.MigratorFailed(logger, migrator.Name, exception);
					failed = true;
				}
#pragma warning restore CA1031
			}
		}

		if (failed)
		{
			// O seed de referência pode ser destrutivo (ADR-0037 remove clients): nunca sobre schema pela metade
			MigrationLog.SeedSkipped(logger);
			return false;
		}

		await serviceProvider.SeedSeccoDataAsync(cancellationToken).ConfigureAwait(false);
		return true;
	}
}

/// <summary>Mensagens de log das migrations (source generator — ADR-0008).</summary>
internal static partial class MigrationLog
{
	[LoggerMessage(EventId = 101, Level = LogLevel.Error, Message = "Migrations: {Migrator} falhou no alvo {Target}.")]
	public static partial void TargetFailed(ILogger logger, string migrator, string target);

	[LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "Migrations: {Migrator} falhou.")]
	public static partial void MigratorFailed(ILogger logger, string migrator, Exception exception);

	[LoggerMessage(EventId = 103, Level = LogLevel.Error, Message = "Seed de referência NÃO executado: houve falha de migration (ADR-0038).")]
	public static partial void SeedSkipped(ILogger logger);
}
