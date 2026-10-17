using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Infrastructure;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// O F5 em Development (e a API do compose ao subir) chama <c>RunSeccoMigrationsAsync()</c> com a
/// configuração do próprio servidor. Se só a credencial de RUNTIME estiver configurada (sem
/// <c>MigrationConnectionString</c>), o migrator cai nela — sem posse do banco. Com as migrations já
/// aplicadas isso não pode falhar: não há DDL a fazer, e o seed de referência é DML sob RLS (ADR-0041).
/// </summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class RuntimeOnlyMigrateTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task RunSeccoMigrations_ComMigracoesAplicadas_SoComCredencialDeRuntime_NaoFalha()
	{
		using var runtimeOnly = factory.WithWebHostBuilder(builder =>
			// Valor vazio = ausente: o migrator usa EffectiveMigrationConnectionString (cai na de runtime)
			builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
				new Dictionary<string, string?> { ["SecureGate:Database:MigrationConnectionString"] = string.Empty })));

		var options = runtimeOnly.Services.GetRequiredService<SecureGateDatabaseOptions>();
		options.EffectiveMigrationConnectionString.Should().Be(factory.GetRuntimeConnectionString(), "sem MigrationConnectionString o migrator usa a credencial de runtime");
		options.EffectiveMigrationConnectionString.Should().NotBe(factory.GetPlatformConnectionString());

		var act = () => runtimeOnly.Services.RunSeccoMigrationsAsync();

		(await act.Should().NotThrowAsync()).Subject.Should().BeTrue("migrations nada pendentes e seed de referência idempotente sob RLS");
	}
}
