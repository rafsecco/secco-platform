using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Secco.SDK.Testing;

/// <summary>
/// Decide e opera a instância de SQL Server que sustenta uma factory de teste: container
/// próprio (padrão) ou instância externa apontada por <c>SECCO_TEST_SQLSERVER</c>.
/// <para>
/// É <c>internal</c> de propósito (ADR-0027): a superfície pública de um pacote sujeito a
/// semver (ADR-0009) fica mínima, e esta lógica permanece testável sem Docker.
/// </para>
/// </summary>
internal sealed partial class SeccoSqlServerInstance : IAsyncDisposable
{
	/// <summary>
	/// Imagem fixada explicitamente: o construtor sem parâmetros do <c>MsSqlBuilder</c> está
	/// obsoleto e some numa versão futura do Testcontainers. É a mesma imagem que ele usava.
	/// </summary>
	private const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04";

	/// <summary>Variável que aponta para uma instância externa; vazia ou ausente = container próprio.</summary>
	internal const string ExternalInstanceVariable = "SECCO_TEST_SQLSERVER";

	private readonly string? _externalConnectionString;
	private MsSqlContainer? _container;
	private readonly HashSet<string> _resolvedDatabases = new(StringComparer.Ordinal);
	private readonly Lock _sync = new();
	private readonly string _suffix;

	public SeccoSqlServerInstance()
		: this(Environment.GetEnvironmentVariable(ExternalInstanceVariable))
	{
	}

	/// <summary>Construtor para teste: recebe o valor da variável em vez de lê-la do ambiente.</summary>
	internal SeccoSqlServerInstance(string? externalConnectionString)
	{
		// O container é construído só no StartAsync: assim CONSTRUIR uma factory não toca o
		// Docker, e os testes desta base rodam sem daemon nenhum (ADR-0027).
		_externalConnectionString = string.IsNullOrWhiteSpace(externalConnectionString)
			? null
			: externalConnectionString;

		// Sufixo por instância de factory: no modo externo, duas suítes concorrentes
		// colidiriam no mesmo servidor. Mantido também no modo container por uniformidade.
		_suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
	}

	/// <summary>Verdadeiro quando a instância aponta para um servidor externo (nenhum container sobe).</summary>
	public bool UsesExternalInstance => _externalConnectionString is not null;

	/// <summary>Sufixo de execução aplicado a todo nome de database desta instância.</summary>
	public string Suffix => _suffix;

	/// <summary>
	/// Resolve o nome final do database (<c>&lt;base&gt;_&lt;sufixo&gt;</c>), validando o nome-base
	/// contra uma allowlist — <c>CREATE</c>/<c>DROP DATABASE</c> não aceitam parametrização,
	/// então a interpolação é inevitável e a validação é a mitigação correta (ADR-0020).
	/// </summary>
	/// <param name="databaseName">Nome-base do database, sem sufixo.</param>
	/// <exception cref="ArgumentException">Nome fora da allowlist.</exception>
	public string ResolveDatabaseName(string databaseName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

		if (!DatabaseNamePattern().IsMatch(databaseName))
		{
			throw new ArgumentException(
				$"Nome de database inválido: '{databaseName}'. Aceito: minúsculas, dígitos e '_', "
				+ "começando por letra, no máximo 91 caracteres.",
				nameof(databaseName));
		}

		var resolved = $"{databaseName}_{_suffix}";

		lock (_sync)
		{
			_resolvedDatabases.Add(resolved);
		}

		return resolved;
	}

	/// <summary>Connection string apontando para um database desta instância.</summary>
	/// <param name="databaseName">Nome-base do database, sem sufixo.</param>
	public string GetConnectionStringFor(string databaseName) =>
		new SqlConnectionStringBuilder(GetServerConnectionString())
		{
			InitialCatalog = ResolveDatabaseName(databaseName),
		}.ConnectionString;

	/// <summary>Connection string do servidor (database default), para comandos de DDL.</summary>
	/// <exception cref="InvalidOperationException">Instância ainda não iniciada.</exception>
	public string GetServerConnectionString() =>
		_externalConnectionString
		?? _container?.GetConnectionString()
		?? throw new InvalidOperationException(
			"A instância de SQL Server ainda não foi iniciada — chame StartAsync() antes.");

	/// <summary>Sobe o container. No modo externo é no-op.</summary>
	public Task StartAsync()
	{
		if (UsesExternalInstance)
		{
			return Task.CompletedTask;
		}

		_container ??= new MsSqlBuilder(DefaultImage).Build();
		return _container.StartAsync();
	}

	/// <summary>
	/// Cria um database vazio, se ainda não existir. Necessário para infraestrutura que não
	/// cria o próprio banco — o Hangfire, por exemplo, só cria o schema dentro de um banco
	/// que já exista (diferente das migrations do EF Core).
	/// </summary>
	/// <param name="databaseName">Nome-base do database, sem sufixo.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task CreateDatabaseAsync(string databaseName, CancellationToken cancellationToken = default)
	{
		var resolved = ResolveDatabaseName(databaseName);

		await using var connection = new SqlConnection(GetServerConnectionString());
		await connection.OpenAsync(cancellationToken);
		await using var command = connection.CreateCommand();
		command.CommandText = $"IF DB_ID('{resolved}') IS NULL CREATE DATABASE [{resolved}]";
		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	/// <summary>
	/// No modo container, descarta o container e tudo vai junto. No modo externo, derruba
	/// best-effort cada database entregue — sem isso o servidor acumularia dezenas por dia.
	/// Falha de limpeza nunca derruba a suíte.
	/// </summary>
	public async ValueTask DisposeAsync()
	{
		if (!UsesExternalInstance)
		{
			if (_container is not null)
			{
				await _container.DisposeAsync();
			}

			return;
		}

		string[] databases;

		lock (_sync)
		{
			databases = [.. _resolvedDatabases];
		}

		foreach (var database in databases)
		{
			try
			{
				await using var connection = new SqlConnection(GetServerConnectionString());
				await connection.OpenAsync();
				await using var command = connection.CreateCommand();
				command.CommandText =
					$"IF DB_ID('{database}') IS NOT NULL BEGIN "
					+ $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; "
					+ $"DROP DATABASE [{database}]; END";
				await command.ExecuteNonQueryAsync();
			}
			catch (SqlException)
			{
				// Limpeza é best-effort: o database pode já ter sumido, ou o servidor
				// externo pode estar indisponível no encerramento. Nunca falha a suíte.
			}
			catch (InvalidOperationException)
			{
				// Idem — connection string do servidor externo inutilizável no encerramento.
			}
		}
	}

	[GeneratedRegex("^[a-z][a-z0-9_]{0,90}$")]
	private static partial Regex DatabaseNamePattern();
}
