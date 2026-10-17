using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Secco.SecureGate.Application.Tenancy;

namespace Secco.SecureGate.Infrastructure.Tenancy;

/// <summary>
/// Leva o <see cref="ITenantAccessScope"/> para dentro do banco (ADR-0041, camada 2): ao abrir cada
/// conexão grava o contexto de sessão que as políticas de Row-Level Security leem. Registrado SÓ no
/// contexto de runtime (DI) — o contexto de migração só faz DDL e não lê entidade.
/// <list type="bullet">
/// <item>Chaves: <c>secco_mode</c> (<c>tenant</c> | <c>platform</c>) e <c>secco_tenant</c> (Guid, só em <c>tenant</c>).
/// Escopo <see cref="TenantAccessMode.None"/> não grava nada: as políticas não deixam nada passar (fail-closed).</item>
/// <item>SQL Server: <c>sp_set_session_context ... @read_only = 1</c> — nem uma injeção na mesma conexão consegue
/// regravar a chave. O <c>sp_reset_connection</c> do pool limpa o contexto, inclusive o somente leitura, quando a
/// conexão é reaproveitada.</item>
/// <item>PostgreSQL: <c>set_config('secco.mode'/'secco.tenant', ..., false)</c> (escopo de sessão). Não existe
/// contexto somente leitura: o RLS segura o filtro esquecido em SQL cru, não comandos empilhados (limite da ADR-0041).
/// O Npgsql executa <c>DISCARD ALL</c> ao devolver a conexão ao pool.</item>
/// </list>
/// <para>
/// <b>Troca de escopo com a conexão aberta.</b> O contexto de sessão é gravado uma vez, na abertura. Se o escopo
/// mudar enquanto a conexão do contexto continua aberta (transação em curso, leitura em streaming, conexão aberta
/// à mão), o banco ainda "acredita" no escopo antigo. Por isso o interceptor guarda o escopo gravado em cada
/// conexão e, antes de QUALQUER comando, compara com o escopo corrente: diferente → <see cref="InvalidOperationException"/>
/// sem dados (fail-closed). O EF abre e fecha a conexão por operação, então o caso normal (plataforma no login →
/// tenant para ler os perfis) reabre e regrava.
/// </para>
/// <para>
/// <b>SQL Server × donos (confirmado em teste, <c>RowLevelSecurityTests</c>):</b> as políticas valem também para
/// <c>db_owner</c> e <c>sysadmin</c> — nenhum dos dois enxerga linha de tenant sem contexto de sessão. A credencial
/// de migração (dona) só faz DDL, mas qualquer leitura de dado por ela, por fora do app (ferramenta SQL, teste),
/// precisa gravar <c>EXEC sp_set_session_context N'secco_mode', N'platform'</c> antes. Quem é dono pode, porém,
/// desligar a política (<c>ALTER SECURITY POLICY</c>) — por isso o runtime nunca pode ser dono.
/// </para>
/// </summary>
public sealed class TenantSessionContextInterceptor(
	ITenantAccessScope accessScope,
	SecureGateDatabaseOptions databaseOptions) : DbConnectionInterceptor, IDbCommandInterceptor
{
	private const string SqlServerCommand =
		"EXEC sp_set_session_context @key = N'secco_mode', @value = @mode, @read_only = 1; "
		+ "IF @tenant IS NOT NULL EXEC sp_set_session_context @key = N'secco_tenant', @value = @tenant, @read_only = 1;";

	private const string PostgresCommand =
		"select set_config('secco.mode', @mode, false), set_config('secco.tenant', coalesce(@tenant, ''), false)";

	private readonly ConditionalWeakTable<DbConnection, GrantedScope> _granted = [];

	/// <inheritdoc />
	public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
	{
		ArgumentNullException.ThrowIfNull(connection);

		try
		{
			using var command = BuildSetCommand(connection);
			command?.ExecuteNonQuery();
		}
		catch
		{
			// Sem contexto gravado a conexão não pode servir ninguém: fecha antes de propagar
			connection.Close();
			throw;
		}
	}

	/// <inheritdoc />
	public override async Task ConnectionOpenedAsync(
		DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(connection);

		try
		{
			await using var command = BuildSetCommand(connection);

			if (command is not null)
			{
				await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}
		}
		catch
		{
			await connection.CloseAsync().ConfigureAwait(false);
			throw;
		}
	}

	/// <inheritdoc />
	public override void ConnectionClosed(DbConnection connection, ConnectionEndEventData eventData) =>
		_granted.Remove(connection);

	/// <inheritdoc />
	public override Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
	{
		_granted.Remove(connection);

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public InterceptionResult<DbDataReader> ReaderExecuting(
		DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
	{
		EnsureScopeUnchanged(command);

		return result;
	}

	/// <inheritdoc />
	public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
		DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
		CancellationToken cancellationToken = default)
	{
		EnsureScopeUnchanged(command);

		return ValueTask.FromResult(result);
	}

	/// <inheritdoc />
	public InterceptionResult<int> NonQueryExecuting(
		DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
	{
		EnsureScopeUnchanged(command);

		return result;
	}

	/// <inheritdoc />
	public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
		DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
		CancellationToken cancellationToken = default)
	{
		EnsureScopeUnchanged(command);

		return ValueTask.FromResult(result);
	}

	/// <inheritdoc />
	public InterceptionResult<object> ScalarExecuting(
		DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
	{
		EnsureScopeUnchanged(command);

		return result;
	}

	/// <inheritdoc />
	public ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
		DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
		CancellationToken cancellationToken = default)
	{
		EnsureScopeUnchanged(command);

		return ValueTask.FromResult(result);
	}

	private DbCommand? BuildSetCommand(DbConnection connection)
	{
		var mode = accessScope.Mode;
		var tenantId = mode == TenantAccessMode.Tenant ? accessScope.TenantId : null;

		_granted.AddOrUpdate(connection, new GrantedScope(mode, tenantId));

		if (mode == TenantAccessMode.None)
		{
			return null;
		}

		var command = connection.CreateCommand();
		command.CommandText = databaseOptions.Provider == SecureGateDatabaseProvider.PostgreSql
			? PostgresCommand
			: SqlServerCommand;

		AddParameter(command, "@mode", mode == TenantAccessMode.Platform ? "platform" : "tenant");
		AddParameter(command, "@tenant", tenantId?.ToString("D"));

		return command;
	}

	private static void AddParameter(DbCommand command, string name, string? value)
	{
		var parameter = command.CreateParameter();
		parameter.ParameterName = name;
		parameter.DbType = System.Data.DbType.String;
		parameter.Value = value is null ? DBNull.Value : value;
		command.Parameters.Add(parameter);
	}

	private void EnsureScopeUnchanged(DbCommand command)
	{
		if (command.Connection is not { } connection || !_granted.TryGetValue(connection, out var granted))
		{
			return;
		}

		var mode = accessScope.Mode;
		var tenantId = mode == TenantAccessMode.Tenant ? accessScope.TenantId : null;

		if (granted.Mode != mode || granted.TenantId != tenantId)
		{
			throw new InvalidOperationException(
				"Troca de escopo de tenant com a conexão do contexto aberta (ADR-0041): o contexto de sessão do banco "
				+ "foi gravado para outro escopo. Feche a operação em curso antes de trocar de escopo.");
		}
	}

	private sealed record GrantedScope(TenantAccessMode Mode, Guid? TenantId);
}
