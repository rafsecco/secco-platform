using Microsoft.Data.SqlClient;
using Secco.SecureGate.Application.Provisioning;

namespace Secco.SecureGate.Infrastructure.Provisioning;

/// <summary>
/// Sonda de conectividade de um banco de tenant no SQL Server, para o painel de status.
/// </summary>
/// <remarks>
/// Usa a conexão de runtime do próprio tenant — nenhuma credencial privilegiada. O que ela
/// responde é exatamente o que o painel precisa saber: o banco cadastrado responde? Metadados de
/// servidor ficaram fora de escopo justamente porque exigiriam privilégio que esta operação não
/// tem e não deve ter.
/// </remarks>
internal sealed class SqlServerTenantDatabaseHealthProbe : ITenantDatabaseHealthProbe
{
	/// <inheritdoc />
	public string Provider => SecureGateDatabaseProviderNames.SqlServer;

	/// <inheritdoc />
	public async Task<TenantDatabaseProbeResult> ProbeAsync(
		string connectionString,
		TimeSpan timeout,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(timeout);

		try
		{
			// O timeout de conexão entra também na própria connection string: sem ele, o driver
			// usa o default de 15s e o CancelAfter não interrompe a fase de handshake.
			var builder = new SqlConnectionStringBuilder(connectionString)
			{
				ConnectTimeout = Math.Max(1, (int)timeout.TotalSeconds),
			};

			await using var connection = new SqlConnection(builder.ConnectionString);
			await connection.OpenAsync(deadline.Token).ConfigureAwait(false);

			await using var command = connection.CreateCommand();
			command.CommandText = "SELECT 1;";
			command.CommandTimeout = Math.Max(1, (int)timeout.TotalSeconds);

			await command.ExecuteScalarAsync(deadline.Token).ConfigureAwait(false);

			return TenantDatabaseProbeResult.Ok;
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return TenantDatabaseProbeResult.Unreachable("tempo limite excedido");
		}
		catch (SqlException exception)
		{
			// Classificação, nunca a mensagem crua: ela traz servidor, banco e usuário (ADR-0020).
			return TenantDatabaseProbeResult.Unreachable(SqlServerTenantDatabaseProvisioner.Classify(exception));
		}
		catch (ArgumentException)
		{
			// Connection string malformada no catálogo — não é falha de rede, e o valor não vaza.
			return TenantDatabaseProbeResult.Unreachable("connection string inválida no catálogo");
		}
	}
}
