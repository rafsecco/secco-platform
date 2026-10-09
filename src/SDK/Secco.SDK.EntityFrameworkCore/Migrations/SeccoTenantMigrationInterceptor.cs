using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// Migra o banco do tenant na PRIMEIRA abertura de conexão no processo (ADR-0038): cobre HTTP,
/// workers e jobs, porque intercepta a conexão e não a requisição. Registrar só em contexto de
/// tenant. O contexto de migração é criado pela fábrica do produto, SEM este interceptor.
/// </summary>
/// <typeparam name="TContext">Contexto de tenant do produto.</typeparam>
public sealed class SeccoTenantMigrationInterceptor<TContext>(
	SeccoTenantMigrationGate gate,
	IServiceProvider serviceProvider,
	Func<IServiceProvider, string, TContext> createMigrationContext) : DbConnectionInterceptor
	where TContext : DbContext
{
	/// <inheritdoc />
	public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
		DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(connection);
		await gate.EnsureMigratedAsync(connection.ConnectionString, MigrateAsync(connection.ConnectionString), cancellationToken)
			.ConfigureAwait(false);
		return result;
	}

	/// <inheritdoc />
	public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
	{
		ArgumentNullException.ThrowIfNull(connection);
		gate.EnsureMigratedAsync(connection.ConnectionString, MigrateAsync(connection.ConnectionString), CancellationToken.None)
			.GetAwaiter().GetResult();
		return result;
	}

	private Func<CancellationToken, Task> MigrateAsync(string connectionString) => async cancellationToken =>
	{
		await using var context = createMigrationContext(serviceProvider, connectionString);

		if ((await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
		{
			await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
		}
	};
}
