using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SDK.EntityFrameworkCore.Migrations;

namespace Secco.NotificationHub.Infrastructure.BackgroundJobs;

/// <summary>
/// Aplica o schema do Hangfire no banco de PLATAFORMA do produto (ADR-0038): fora de Development
/// o servidor sobe com <c>PrepareSchemaIfNecessary = false</c> e quem cria/atualiza o schema é o
/// verbo <c>migrate</c>. Usa a credencial de migração quando existe (a de runtime não precisa de DDL).
/// </summary>
internal sealed partial class HangfireSchemaMigrator(
	IOptions<NotificationHubBackgroundJobOptions> options,
	ILogger<HangfireSchemaMigrator> logger) : ISeccoDatabaseMigrator
{
	/// <summary>Alvo devolvido na lista de falhas (nunca a connection string).</summary>
	internal const string FailureTarget = "hangfire";

	public string Name => "NotificationHub (Hangfire)";

	public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
	{
		var settings = options.Value;
		var connectionString = string.IsNullOrWhiteSpace(settings.MigrationConnectionString)
			? settings.ConnectionString
			: settings.MigrationConnectionString;

		try
		{
			await using var connection = new SqlConnection(connectionString);
			await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

			// O instalador é idempotente (cria ou atualiza, com lock de aplicação) e síncrono
			SqlServerObjectsInstaller.Install(connection);
			return [];
		}
#pragma warning disable CA1031 // Falha do schema vira alvo na lista; a exceção é logada sem a connection string
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			LogFailed(logger, exception);
			return [FailureTarget];
		}
#pragma warning restore CA1031
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Migrations do NotificationHub: falha ao aplicar o schema do Hangfire.")]
	private static partial void LogFailed(ILogger logger, Exception exception);
}
