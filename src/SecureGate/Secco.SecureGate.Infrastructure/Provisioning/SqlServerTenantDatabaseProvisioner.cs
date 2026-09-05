using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Secco.SecureGate.Application.Provisioning;

namespace Secco.SecureGate.Infrastructure.Provisioning;

/// <summary>
/// Provisionamento de banco de tenant no SQL Server (ADR-0018: provider padrão da plataforma).
/// </summary>
/// <remarks>
/// O script e a execução automática usam <b>o mesmo</b> texto SQL, de propósito: dois caminhos
/// separados poderiam divergir, e a divergência apareceria só em produção, no modo que o adotante
/// usa e o desenvolvedor não.
/// <para>
/// Todo identificador passa por <see cref="DatabaseIdentifierPolicy"/> antes de ser concatenado —
/// DDL não aceita parâmetro para nome de objeto, então a allowlist é a única barreira contra
/// injeção com privilégio de administrador do servidor (ADR-0020). A senha, essa sim, entra como
/// literal escapado, e o alfabeto do gerador já exclui aspas.
/// </para>
/// </remarks>
internal sealed class SqlServerTenantDatabaseProvisioner : ITenantDatabaseProvisioner
{
	/// <inheritdoc />
	public string Provider => SecureGateDatabaseProviderNames.SqlServer;

	/// <inheritdoc />
	public string BuildScript(TenantDatabaseProvisioningPlan plan)
	{
		ArgumentNullException.ThrowIfNull(plan);

		var database = DatabaseIdentifierPolicy.QuoteSqlServer(plan.DatabaseName);
		var login = DatabaseIdentifierPolicy.QuoteSqlServer(plan.LoginName);
		var password = EscapeLiteral(plan.Password);
		var loginLiteral = EscapeLiteral(plan.LoginName);
		var databaseLiteral = EscapeLiteral(plan.DatabaseName);

		var script = new StringBuilder();

		script.AppendLine(CultureInfo.InvariantCulture,
			$"-- Provisionamento do banco de tenant '{plan.DatabaseName}' (Secco.SecureGate).");
		script.AppendLine("-- O usuário criado recebe db_owner NO PRÓPRIO BANCO e nada no servidor:");
		script.AppendLine("-- é o que transforma o isolamento entre tenants de convenção em garantia.");
		script.AppendLine();

		if (plan.CreateDatabase)
		{
			script.AppendLine(CultureInfo.InvariantCulture,
				$"IF DB_ID(N'{databaseLiteral}') IS NULL");
			script.AppendLine(CultureInfo.InvariantCulture, $"    CREATE DATABASE {database};");
			script.AppendLine("GO");
			script.AppendLine();
		}

		script.AppendLine(CultureInfo.InvariantCulture,
			$"IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'{loginLiteral}')");
		script.AppendLine(CultureInfo.InvariantCulture,
			$"    CREATE LOGIN {login} WITH PASSWORD = N'{password}', CHECK_POLICY = OFF;");
		script.AppendLine("GO");
		script.AppendLine();

		script.AppendLine(CultureInfo.InvariantCulture, $"USE {database};");
		script.AppendLine("GO");
		script.AppendLine();

		script.AppendLine(CultureInfo.InvariantCulture,
			$"IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{loginLiteral}')");
		script.AppendLine(CultureInfo.InvariantCulture, $"    CREATE USER {login} FOR LOGIN {login};");
		script.AppendLine("GO");
		script.AppendLine();

		script.AppendLine(CultureInfo.InvariantCulture, $"ALTER ROLE db_owner ADD MEMBER {login};");
		script.AppendLine("GO");

		return script.ToString();
	}

	/// <inheritdoc />
	public string BuildTenantConnectionString(TenantDatabaseProvisioningPlan plan)
	{
		ArgumentNullException.ThrowIfNull(plan);

		// O builder cuida do escaping de cada valor — montar por concatenação seria repetir à mão
		// uma regra que a BCL já implementa corretamente.
		var builder = new SqlConnectionStringBuilder
		{
			DataSource = plan.Server,
			InitialCatalog = plan.DatabaseName,
			UserID = plan.LoginName,
			Password = plan.Password,
			TrustServerCertificate = true,
			MultipleActiveResultSets = false,
		};

		return builder.ConnectionString;
	}

	/// <inheritdoc />
	public async Task<TenantDatabaseProvisioningExecution> ExecuteAsync(
		TenantDatabaseProvisioningPlan plan,
		string adminConnectionString,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentException.ThrowIfNullOrWhiteSpace(adminConnectionString);

		try
		{
			await using var connection = new SqlConnection(adminConnectionString);
			await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

			// O script é dividido em lotes por GO, que é diretiva de ferramenta e não T-SQL:
			// enviá-lo inteiro num único comando falharia.
			foreach (var batch in SplitBatches(BuildScript(plan)))
			{
				await using var command = connection.CreateCommand();
				command.CommandText = batch;
				command.CommandTimeout = 120;

				await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
			}

			return TenantDatabaseProvisioningExecution.Success;
		}
		catch (SqlException exception)
		{
			// Classificação, nunca a mensagem crua: ela costuma trazer servidor e usuário (ADR-0020).
			return TenantDatabaseProvisioningExecution.Failed(Classify(exception));
		}
	}

	/// <summary>Traduz o erro do servidor em uma causa acionável, sem vazar detalhe de conexão.</summary>
	/// <param name="exception">Exceção original do SQL Server.</param>
	internal static string Classify(SqlException exception) => exception.Number switch
	{
		18456 => "credencial de provisionamento recusada pelo servidor",
		262 or 15247 => "a credencial de provisionamento não tem permissão para esta operação",
		1801 => "o database já existe no servidor",
		15025 => "o login já existe no servidor",
		4060 => "banco de destino inexistente ou inacessível para este login",
		53 or -1 or 10060 or 10061 => "servidor de banco inacessível",
		_ => "erro do servidor de banco ao aplicar o provisionamento",
	};

	/// <summary>Escapa um literal de string T-SQL, dobrando as aspas simples.</summary>
	/// <param name="value">Valor a escapar.</param>
	private static string EscapeLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);

	/// <summary>Divide o script nos separadores <c>GO</c>, descartando lotes vazios.</summary>
	/// <param name="script">Script completo.</param>
	private static IEnumerable<string> SplitBatches(string script)
	{
		var batch = new StringBuilder();

		foreach (var line in script.Split('\n'))
		{
			if (string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))
			{
				var completed = batch.ToString().Trim();
				batch.Clear();

				if (completed.Length > 0)
				{
					yield return completed;
				}

				continue;
			}

			batch.AppendLine(line);
		}

		var tail = batch.ToString().Trim();

		if (tail.Length > 0)
		{
			yield return tail;
		}
	}
}
