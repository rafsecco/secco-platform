using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Secco.SecureGate.Infrastructure.Contexts;

namespace Secco.SecureGate.Infrastructure;

/// <summary>
/// Recusa subir com a credencial de RUNTIME dona do banco de plataforma (ADR-0041, emenda de
/// 2026-10-17). Sem isso o Row-Level Security seria decorativo: quem é dono desliga a política.
/// <para>
/// A checagem roda com a <b>credencial de runtime</b> (<c>ConnectionString</c>), nunca com a de
/// migração: o que se verifica é justamente o login que o servidor usará. Por isso ela vive na
/// subida do servidor e NÃO no verbo <c>migrate</c> — este usa a credencial de migração para o DDL
/// e não reprova a si mesmo.
/// </para>
/// </summary>
public static class SecureGateRuntimeCredentialCheck
{
	/// <summary>Tabelas protegidas pela ADR-0041 (as 9 da rodada 1, mais as das ADR-0044 e ADR-0054).</summary>
	private static readonly string[] ProtectedTables =
	[
		"tb_roles", "tb_role_claims", "tb_user_roles", "tb_tenant_memberships", "tb_users",
		"tb_tenant_databases", "tb_tenant_federations", "tb_tenant_group_role_mappings",
		"tb_elevation_grants", "tb_tenant_admin_grants", "tb_authority_audit_entries",
	];

	// Consultas fixas, sem nenhum valor externo (ADR-0020). Cada coluna é um motivo de reprovação.
	internal const string SqlServerQuery =
		"SELECT "
		+ "IS_ROLEMEMBER(N'db_owner'), "
		+ "IS_SRVROLEMEMBER(N'sysadmin'), "
		+ "HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'ALTER ANY SECURITY POLICY'), "
		+ "HAS_PERMS_BY_NAME(DB_NAME(), N'DATABASE', N'CONTROL')";

	internal static readonly string PostgresQuery =
		"SELECT r.rolsuper, r.rolbypassrls, "
		+ "EXISTS (SELECT 1 FROM pg_tables t WHERE t.schemaname = ANY (current_schemas(false)) "
		+ "AND t.tablename IN (" + string.Join(", ", ProtectedTables.Select(table => $"'{table}'")) + ") "
		+ "AND pg_has_role(current_user, t.tableowner, 'USAGE')) "
		+ "FROM pg_roles r WHERE r.rolname = current_user";

	/// <summary>
	/// Confere a credencial de runtime. Fora de Development, privilégio indevido lança
	/// <see cref="InvalidOperationException"/>; em Development só registra <c>Warning</c>.
	/// </summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="isDevelopment">Ambiente é Development.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task EnsureRuntimeIsNotOwnerAsync(
		this IServiceProvider serviceProvider,
		bool isDevelopment,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var databaseOptions = scope.ServiceProvider.GetRequiredService<SecureGateDatabaseOptions>();

		if (!databaseOptions.VerifyRuntimeIsNotOwner)
		{
			return;
		}

		var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
			.CreateLogger(typeof(SecureGateRuntimeCredentialCheck));

		await VerifyAsync(databaseOptions, isDevelopment, logger, cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Núcleo testável: abre a conexão de runtime, consulta e decide.</summary>
	/// <param name="databaseOptions">Opções do banco de plataforma.</param>
	/// <param name="isDevelopment">Ambiente é Development.</param>
	/// <param name="logger">Logger do aviso.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task VerifyAsync(
		SecureGateDatabaseOptions databaseOptions,
		bool isDevelopment,
		ILogger logger,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(databaseOptions);
		ArgumentNullException.ThrowIfNull(logger);

		// Credencial de RUNTIME, em contexto próprio: só Database.* é usado, nunca entidade
		var options = SecureGateDatabaseProviderConfigurator.CreateOptions(
			databaseOptions.Provider, databaseOptions.ConnectionString!);

		await using var context = new SecureGateDbContext(options);
		var connection = context.Database.GetDbConnection();
		await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

		var reasons = databaseOptions.Provider == SecureGateDatabaseProvider.PostgreSql
			? await ReadPostgresReasonsAsync(connection, cancellationToken).ConfigureAwait(false)
			: await ReadSqlServerReasonsAsync(connection, cancellationToken).ConfigureAwait(false);

		if (reasons.Count == 0)
		{
			return;
		}

		var message =
			$"A credencial de runtime do banco de plataforma do SecureGate tem privilégio de dono ({string.Join("; ", reasons)}). "
			+ "Configure 'SecureGate:Database:ConnectionString' com um login sem posse do banco "
			+ "(SQL Server: db_datareader + db_datawriter; PostgreSQL: não superusuário, sem BYPASSRLS e não dono das tabelas) "
			+ "e informe o login dono em 'SecureGate:Database:MigrationConnectionString', usado só pelo 'migrate' (ADR-0041).";

		if (isDevelopment)
		{
			RuntimeCredentialLog.OwnerCredential(logger, message);
			return;
		}

		throw new InvalidOperationException(message);
	}

	private static async Task<List<string>> ReadSqlServerReasonsAsync(DbConnection connection, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = SqlServerQuery;
		command.CommandType = CommandType.Text;

		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

		var reasons = new List<string>();
		AddIf(reasons, reader, 0, "membro de db_owner");
		AddIf(reasons, reader, 1, "membro de sysadmin");
		AddIf(reasons, reader, 2, "permissão ALTER ANY SECURITY POLICY");
		AddIf(reasons, reader, 3, "permissão CONTROL no banco");

		return reasons;
	}

	private static async Task<List<string>> ReadPostgresReasonsAsync(DbConnection connection, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = PostgresQuery;
		command.CommandType = CommandType.Text;

		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
		await reader.ReadAsync(cancellationToken).ConfigureAwait(false);

		var reasons = new List<string>();
		AddIf(reasons, reader, 0, "superusuário");
		AddIf(reasons, reader, 1, "BYPASSRLS");
		AddIf(reasons, reader, 2, "dono de tabela protegida");

		return reasons;
	}

	// IS_ROLEMEMBER/HAS_PERMS_BY_NAME devolvem int (ou NULL se o papel não existe); Postgres devolve bool
	private static void AddIf(List<string> reasons, DbDataReader reader, int ordinal, string reason)
	{
		if (reader.IsDBNull(ordinal))
		{
			return;
		}

		var value = reader.GetValue(ordinal);
		var flagged = value switch
		{
			bool boolean => boolean,
			int integer => integer == 1,
			_ => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 1,
		};

		if (flagged)
		{
			reasons.Add(reason);
		}
	}
}

/// <summary>Mensagens de log da checagem de credencial (source generator — ADR-0008).</summary>
internal static partial class RuntimeCredentialLog
{
	[LoggerMessage(EventId = 201, Level = LogLevel.Warning, Message = "{Detail}")]
	public static partial void OwnerCredential(ILogger logger, string detail);
}
