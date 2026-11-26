using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.NotificationHub.Infrastructure;
using Secco.NotificationHub.Infrastructure.BackgroundJobs;
using Xunit;

namespace Secco.NotificationHub.Tests.Integration;

/// <summary>
/// Schema do Hangfire aplicado pelo <c>migrate</c> e conferido (só leitura) na subida fora de
/// DEV (ADR-0038). Cada teste usa um banco vazio próprio na instância compartilhada.
/// </summary>
[Collection(NotificationHubApiCollectionDefinition.Name)]
public class HangfireSchemaTests(NotificationHubApiFactory factory)
{
	private const string UnreachableConnectionString =
		"Server=127.0.0.1,1;Database=x;User Id=sa;Password=x;Connect Timeout=2;Encrypt=False";

	[Fact]
	public async Task Migrate_BancoVazio_CriaSchemaDoHangfire()
	{
		var connectionString = await NewEmptyDatabaseAsync();

		var failures = await CreateMigrator(connectionString).MigrateAsync();

		failures.Should().BeEmpty();
		(await SchemaVersionAsync(connectionString)).Should().NotBeNull();
	}

	[Fact]
	public async Task Migrate_Repetido_Idempotente()
	{
		var connectionString = await NewEmptyDatabaseAsync();
		var migrator = CreateMigrator(connectionString);

		(await migrator.MigrateAsync()).Should().BeEmpty();
		(await migrator.MigrateAsync()).Should().BeEmpty();

		(await SchemaVersionAsync(connectionString)).Should().NotBeNull();
	}

	[Fact]
	public async Task Migrate_ServidorInalcancavel_DevolveAlvoHangfire()
	{
		var failures = await CreateMigrator(UnreachableConnectionString).MigrateAsync();

		failures.Should().Equal("hangfire");
	}

	[Fact]
	public async Task Migrate_UsaMigrationConnectionStringQuandoPresente()
	{
		var connectionString = await NewEmptyDatabaseAsync();

		var failures = await CreateMigrator(UnreachableConnectionString, migrationConnectionString: connectionString).MigrateAsync();

		failures.Should().BeEmpty();
		(await SchemaVersionAsync(connectionString)).Should().NotBeNull();
	}

	[Fact]
	public async Task EnsureSchema_Ausente_Lanca()
	{
		var connectionString = await NewEmptyDatabaseAsync();

		var act = () => CreateProvider(connectionString).EnsureNotificationHubJobSchemaAsync();

		(await act.Should().ThrowAsync<InvalidOperationException>())
			.WithMessage("*dotnet Secco.NotificationHub.Api.dll migrate*");
	}

	[Fact]
	public async Task EnsureSchema_Presente_NaoLanca()
	{
		var connectionString = await NewEmptyDatabaseAsync();
		(await CreateMigrator(connectionString).MigrateAsync()).Should().BeEmpty();

		var act = () => CreateProvider(connectionString).EnsureNotificationHubJobSchemaAsync();

		await act.Should().NotThrowAsync();
	}

	private async Task<string> NewEmptyDatabaseAsync()
	{
		var name = $"secco_nh_hf_{Guid.NewGuid():N}";
		await factory.CreateDatabaseAsync(name);
		return factory.GetConnectionStringFor(name);
	}

	private static HangfireSchemaMigrator CreateMigrator(string connectionString, string? migrationConnectionString = null) =>
		new(
			Options.Create(new NotificationHubBackgroundJobOptions
			{
				ConnectionString = connectionString,
				MigrationConnectionString = migrationConnectionString,
			}),
			NullLogger<HangfireSchemaMigrator>.Instance);

	private static ServiceProvider CreateProvider(string connectionString)
	{
		var services = new ServiceCollection();
		services.AddSingleton(Options.Create(new NotificationHubBackgroundJobOptions { ConnectionString = connectionString }));
		return services.BuildServiceProvider();
	}

	private static async Task<int?> SchemaVersionAsync(string connectionString)
	{
		await using var connection = new SqlConnection(connectionString);
		await connection.OpenAsync();
		await using var command = new SqlCommand("SELECT MAX([Version]) FROM [HangFire].[Schema]", connection);
		var value = await command.ExecuteScalarAsync();
		return value is int version ? version : null;
	}
}
