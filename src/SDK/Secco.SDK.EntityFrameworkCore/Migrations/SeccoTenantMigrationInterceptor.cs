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
		var connectionString = ConnectionStringOf(eventData);
		if (connectionString.Length == 0)
		{
			return result;
		}

		await gate.EnsureMigratedAsync(connectionString, MigrateAsync(connectionString), cancellationToken)
			.ConfigureAwait(false);
		return result;
	}

	/// <inheritdoc />
	public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
	{
		ArgumentNullException.ThrowIfNull(connection);
		var connectionString = ConnectionStringOf(eventData);
		if (connectionString.Length == 0)
		{
			return result;
		}

		gate.EnsureMigratedAsync(connectionString, MigrateAsync(connectionString), CancellationToken.None)
			.GetAwaiter().GetResult();
		return result;
	}

	// A string configurada no contexto, e não a do DbConnection: depois da primeira abertura o
	// provider devolve a string SEM a senha (Persist Security Info=False), e a segunda abertura
	// da mesma conexão geraria outra chave no gate e uma migração sem credencial (tenant em 503
	// permanente). Sem a string do contexto NÃO há fallback para a da conexão, pela mesma razão:
	// ela pode já vir sem senha. Nesse caso o interceptor simplesmente não age.
	private static string ConnectionStringOf(ConnectionEventData eventData) =>
		eventData.Context?.Database.GetConnectionString() ?? string.Empty;

	private Func<CancellationToken, Task> MigrateAsync(string connectionString) => async cancellationToken =>
	{
		await using var context = createMigrationContext(serviceProvider, connectionString);

		if ((await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
		{
			await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
		}
	};
}
