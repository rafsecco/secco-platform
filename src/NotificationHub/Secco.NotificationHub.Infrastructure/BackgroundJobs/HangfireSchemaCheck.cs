using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Secco.NotificationHub.Infrastructure.BackgroundJobs;

/// <summary>Conferência, só leitura, do schema do Hangfire na subida fora de Development (ADR-0038).</summary>
public static class HangfireSchemaCheck
{
	private const string ExistsSql = "SELECT CASE WHEN OBJECT_ID(N'[HangFire].[Schema]', N'U') IS NULL THEN 0 ELSE 1 END";

	private const string VersionSql = "SELECT MAX([Version]) FROM [HangFire].[Schema]";

	/// <summary>
	/// Recusa subir se <c>[HangFire].[Schema]</c> não existe ou está vazia. Só leitura: quem aplica
	/// é o <c>migrate</c>.
	/// </summary>
	/// <remarks>
	/// O <c>Hangfire.SqlServer</c> 1.8.25 NÃO expõe a versão exigida de forma utilizável:
	/// <c>SqlServerObjectsInstaller.RequiredSchemaVersion</c> é público porém <c>[Obsolete]</c> ("unused,
	/// will be removed in 2.0.0") e <c>LatestSchemaVersion</c> é a versão que o instalador alcança, não
	/// um mínimo de operação (migrações pesadas ficam desligadas por padrão). Por isso a checagem é só
	/// "a tabela existe e tem uma linha"; schema defasado após atualizar o pacote é corrigido
	/// reexecutando o <c>migrate</c>, que o instalador torna idempotente.
	/// </remarks>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task EnsureNotificationHubJobSchemaAsync(
		this IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		var connectionString = serviceProvider
			.GetRequiredService<IOptions<NotificationHubBackgroundJobOptions>>().Value.ConnectionString;

		await using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

		int? version = null;

		await using (var exists = new SqlCommand(ExistsSql, connection))
		{
			if ((int)(await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! == 1)
			{
				await using var read = new SqlCommand(VersionSql, connection);
				var value = await read.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
				version = value is int found ? found : null;
			}
		}

		if (version is null)
		{
			throw new InvalidOperationException(
				"O schema do Hangfire do NotificationHub está ausente ou desatualizado. Rode 'dotnet Secco.NotificationHub.Api.dll migrate' antes de subir (ADR-0062).");
		}
	}
}
