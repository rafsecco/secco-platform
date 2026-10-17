using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Secco.SecureGate.Infrastructure;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Recusa de subir com a credencial de runtime dona do banco (ADR-0041, emenda de 2026-10-17):
/// fora de Development reprova, em Development só avisa, e o login sem posse sobe.
/// </summary>
public class RuntimeCredentialCheckTests
{
	/// <summary>Runtime como <c>db_owner</c>: a configuração que a ADR-0041 proíbe.</summary>
	private sealed class OwnerRuntimeFactory : SecureGateApiFactory
	{
		protected override string[] RuntimeDatabaseRoles => ["db_owner"];
	}

	private sealed class OwnerRuntimeWithoutCheckFactory : SecureGateApiFactory
	{
		protected override string[] RuntimeDatabaseRoles => ["db_owner"];

		protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
		{
			base.ConfigureTestConfiguration(settings);
			settings["SecureGate:Database:VerifyRuntimeIsNotOwner"] = "false";
		}
	}

	private sealed class CapturingLogger : ILogger
	{
		public List<(LogLevel Level, string Message)> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Entries.Add((logLevel, formatter(state, exception)));
	}

	private static SecureGateDatabaseOptions OptionsFor(string runtimeConnectionString) =>
		new() { ConnectionString = runtimeConnectionString };

	[Fact]
	public async Task Startup_ForaDeDevComRuntimeDbOwner_RecusaSubir()
	{
		var factory = new OwnerRuntimeFactory();
		await factory.InitializeAsync();

		try
		{
			var act = () => factory.CreateClient();

			var thrown = act.Should().Throw<InvalidOperationException>().Which;
			thrown.Message.Should().Contain("db_owner").And.Contain("SecureGate:Database:ConnectionString");
			thrown.Message.Should().NotContain("Password", "a mensagem nunca carrega a connection string (ADR-0020)");
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}

	[Fact]
	public async Task Startup_ForaDeDevComRuntimeSemPosse_Sobe()
	{
		var factory = new SecureGateApiFactory();
		await factory.InitializeAsync();

		try
		{
			var act = () => factory.CreateClient();

			act.Should().NotThrow();
			// Prova direta: o login de runtime não consegue fazer DDL no banco
			await using var connection = new SqlConnection(factory.GetRuntimeConnectionString());
			await connection.OpenAsync();
			await using var command = connection.CreateCommand();
			command.CommandText = "CREATE TABLE tb_prova_de_posse (id int)";
			var ddl = async () => await command.ExecuteNonQueryAsync();
			await ddl.Should().ThrowAsync<SqlException>();
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}

	[Fact]
	public async Task Startup_ComVerificacaoDesligada_RuntimeDbOwnerSobe()
	{
		var factory = new OwnerRuntimeWithoutCheckFactory();
		await factory.InitializeAsync();

		try
		{
			var act = () => factory.CreateClient();

			act.Should().NotThrow();
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}

	[Fact]
	public async Task Verify_EmDevelopmentComRuntimeDono_SoAvisa()
	{
		var factory = new SecureGateApiFactory();
		await factory.InitializeAsync();

		try
		{
			var logger = new CapturingLogger();

			// O admin da instância (sa) é sysadmin: dono em todos os sentidos
			await SecureGateRuntimeCredentialCheck.VerifyAsync(
				OptionsFor(factory.GetPlatformConnectionString()), isDevelopment: true, logger);

			logger.Entries.Should().ContainSingle(entry => entry.Level == LogLevel.Warning)
				.Which.Message.Should().Contain("sysadmin").And.Contain("MigrationConnectionString");
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}

	[Fact]
	public async Task Verify_ForaDeDevComRuntimeSysadmin_Lanca()
	{
		var factory = new SecureGateApiFactory();
		await factory.InitializeAsync();

		try
		{
			var act = () => SecureGateRuntimeCredentialCheck.VerifyAsync(
				OptionsFor(factory.GetPlatformConnectionString()), isDevelopment: false, new CapturingLogger());

			(await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("sysadmin");
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}

	[Fact]
	public async Task Verify_RuntimeSemPosse_NaoAvisaNemLanca()
	{
		var factory = new SecureGateApiFactory();
		await factory.InitializeAsync();

		try
		{
			var logger = new CapturingLogger();

			await SecureGateRuntimeCredentialCheck.VerifyAsync(
				OptionsFor(factory.GetRuntimeConnectionString()), isDevelopment: false, logger);

			logger.Entries.Should().BeEmpty();
		}
		finally
		{
			await ((IAsyncLifetime)factory).DisposeAsync();
		}
	}

	[Fact]
	public void Opcoes_SemMigrationConnectionString_UsamARuntime()
	{
		OptionsFor("Server=x;Database=runtime").EffectiveMigrationConnectionString.Should().Be("Server=x;Database=runtime");

		new SecureGateDatabaseOptions { ConnectionString = "Server=x;Database=runtime", MigrationConnectionString = "Server=x;Database=migrador" }
			.EffectiveMigrationConnectionString.Should().Be("Server=x;Database=migrador");
	}
}
