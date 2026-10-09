using FluentAssertions;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Migrations;

public class SeccoTenantMigrationGateTests
{
	private const string ConnectionString = "Server=db;Database=tenant_a;User Id=app;Password=segredo-nao-pode-vazar";

	[Fact]
	public async Task EnsureMigrated_ChamadasConcorrentes_MigraUmaVez()
	{
		var gate = new SeccoTenantMigrationGate();
		var calls = 0;
		var release = new TaskCompletionSource();

		async Task Migrate(CancellationToken _) { Interlocked.Increment(ref calls); await release.Task; }

		var first = gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		var second = gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		release.SetResult();
		await Task.WhenAll(first, second);

		calls.Should().Be(1);
	}

	[Fact]
	public async Task EnsureMigrated_JaConferido_NaoChamaDeNovo()
	{
		var gate = new SeccoTenantMigrationGate();
		var calls = 0;
		Task Migrate(CancellationToken _) { calls++; return Task.CompletedTask; }

		await gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		await gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);

		calls.Should().Be(1);
	}

	[Fact]
	public async Task EnsureMigrated_Falha_LancaTransitoriaEPermiteNovaTentativa()
	{
		var clock = new ManualTimeProvider();
		var gate = new SeccoTenantMigrationGate(clock);
		var attempt = 0;
		Task Migrate(CancellationToken _) => ++attempt == 1 ? throw new InvalidOperationException("servidor fora") : Task.CompletedTask;

		var first = () => gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		var thrown = await first.Should().ThrowAsync<TenantDatabaseUnavailableException>();
		thrown.Which.InnerException.Should().BeOfType<InvalidOperationException>();
		thrown.Which.Message.Should().NotContain("segredo");

		clock.Advance(SeccoTenantMigrationGate.FailureBackoff + TimeSpan.FromSeconds(1));
		await gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		attempt.Should().Be(2, "falha não é memorizada");
	}

	[Fact]
	public async Task EnsureMigrated_DentroDaJanelaDeBackoff_LancaSemChamarMigrate()
	{
		var clock = new ManualTimeProvider();
		var gate = new SeccoTenantMigrationGate(clock);
		var calls = 0;
		Task Migrate(CancellationToken _) { calls++; throw new InvalidOperationException("servidor fora"); }

		var call = () => gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		await call.Should().ThrowAsync<TenantDatabaseUnavailableException>();
		clock.Advance(SeccoTenantMigrationGate.FailureBackoff - TimeSpan.FromSeconds(1));
		var second = await call.Should().ThrowAsync<TenantDatabaseUnavailableException>();

		calls.Should().Be(1);
		second.Which.Message.Should().NotContain("segredo");
	}

	[Fact]
	public async Task EnsureMigrated_AposAJanela_TentaMigrarDeNovo()
	{
		var clock = new ManualTimeProvider();
		var gate = new SeccoTenantMigrationGate(clock);
		var calls = 0;
		Task Migrate(CancellationToken _) { calls++; throw new InvalidOperationException("servidor fora"); }

		var call = () => gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		await call.Should().ThrowAsync<TenantDatabaseUnavailableException>();
		clock.Advance(SeccoTenantMigrationGate.FailureBackoff);
		await call.Should().ThrowAsync<TenantDatabaseUnavailableException>();

		calls.Should().Be(2);
	}

	[Fact]
	public async Task EnsureMigrated_SucessoAposAJanela_LimpaOEstadoDeFalha()
	{
		var clock = new ManualTimeProvider();
		var gate = new SeccoTenantMigrationGate(clock);
		var calls = 0;
		Task Migrate(CancellationToken _) => ++calls == 1 ? throw new InvalidOperationException("servidor fora") : Task.CompletedTask;

		var call = () => gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		await call.Should().ThrowAsync<TenantDatabaseUnavailableException>();
		clock.Advance(SeccoTenantMigrationGate.FailureBackoff);
		await gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		await gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);

		calls.Should().Be(2);
	}

	private sealed class ManualTimeProvider : TimeProvider
	{
		private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => _now;

		public void Advance(TimeSpan by) => _now += by;
	}

	[Fact]
	public async Task EnsureMigrated_ChaveGuardada_NaoContemAConnectionString()
	{
		var gate = new SeccoTenantMigrationGate();

		await gate.EnsureMigratedAsync(ConnectionString, _ => Task.CompletedTask, CancellationToken.None);

		gate.Keys.Should().ContainSingle().Which.Should().NotContain("segredo").And.HaveLength(64, "SHA-256 em hex");
	}

	[Fact]
	public async Task EnsureMigrated_BancosDiferentes_MigraCadaUm()
	{
		var gate = new SeccoTenantMigrationGate();
		var calls = 0;
		Task Migrate(CancellationToken _) { calls++; return Task.CompletedTask; }

		await gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		await gate.EnsureMigratedAsync(ConnectionString.Replace("tenant_a", "tenant_b", StringComparison.Ordinal), Migrate, CancellationToken.None);

		calls.Should().Be(2);
	}
}
