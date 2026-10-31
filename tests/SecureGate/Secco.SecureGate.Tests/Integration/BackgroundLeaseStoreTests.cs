using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Secco.SecureGate.Application.Scheduling;
using Secco.SecureGate.Infrastructure.Scheduling;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Contrato do lease de tarefa periódica (ADR-0055), executado nos DOIS engines (ADR-0018): tomada, renovação e
/// encerramento atômicos, sem retomar lease válido e com uma só vencedora na corrida.
/// </summary>
public abstract class BackgroundLeaseStoreContract
{
	/// <summary>Uma store sobre um contexto novo: cada chamada é uma "instância" de aplicação diferente.</summary>
	protected abstract (IBackgroundLeaseStore Store, IDisposable Handle) NewStore();

	private static string Task_() => $"tarefa-{Guid.NewGuid():N}";

	private async Task<bool> AcquireAsync(string task, string owner, TimeSpan duration, DateTimeOffset now)
	{
		var (store, handle) = NewStore();
		using (handle)
		{
			return await store.TryAcquireAsync(task, owner, duration, now);
		}
	}

	[Fact]
	public async Task PrimeiraTomada_CriaOLease()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;

		(await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now)).Should().BeTrue();
	}

	[Fact]
	public async Task LeaseValido_NaoETomadoPorOutro_NemPeloPropriaDono()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;
		await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now);

		(await AcquireAsync(task, "b", TimeSpan.FromMinutes(30), now.AddMinutes(10))).Should().BeFalse();
		(await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now.AddMinutes(10))).Should().BeFalse(
			"é o lease válido que impede uma segunda passada no mesmo intervalo");
	}

	[Fact]
	public async Task LeaseExpirado_ETomadoPorOutraInstancia()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;
		await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now);

		(await AcquireAsync(task, "b", TimeSpan.FromMinutes(30), now.AddMinutes(31))).Should().BeTrue();
		(await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now.AddMinutes(32))).Should().BeFalse("agora é de b");
	}

	[Fact]
	public async Task Renovar_SoValeParaODono()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;
		await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now);

		var (store, handle) = NewStore();
		using (handle)
		{
			(await store.RenewAsync(task, "b", TimeSpan.FromMinutes(30), now.AddMinutes(20))).Should().BeFalse();
			(await store.RenewAsync(task, "a", TimeSpan.FromMinutes(30), now.AddMinutes(20))).Should().BeTrue();
		}

		// Renovado em +20 min por 30 min: ainda vale em +40 min
		(await AcquireAsync(task, "b", TimeSpan.FromMinutes(30), now.AddMinutes(40))).Should().BeFalse();
		(await AcquireAsync(task, "b", TimeSpan.FromMinutes(30), now.AddMinutes(51))).Should().BeTrue();
	}

	[Fact]
	public async Task Completar_DeixaOLeaseValidoAteAProximaExecucaoDevida()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;
		await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now);

		var (store, handle) = NewStore();
		using (handle)
		{
			await store.CompleteAsync(task, "a", now.AddHours(24));
		}

		(await AcquireAsync(task, "b", TimeSpan.FromMinutes(30), now.AddHours(23))).Should().BeFalse();
		(await AcquireAsync(task, "b", TimeSpan.FromMinutes(30), now.AddHours(24).AddSeconds(1))).Should().BeTrue();
	}

	[Fact]
	public async Task Completar_ComInstanteVencido_LiberaATarefaNaHora()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;
		await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now);

		var (store, handle) = NewStore();
		using (handle)
		{
			await store.CompleteAsync(task, "a", now);
		}

		(await AcquireAsync(task, "b", TimeSpan.FromMinutes(30), now.AddSeconds(1))).Should().BeTrue();
	}

	[Fact]
	public async Task Completar_PorQuemNaoEDono_NaoMudaNada()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;
		await AcquireAsync(task, "a", TimeSpan.FromMinutes(30), now);

		var (store, handle) = NewStore();
		using (handle)
		{
			await store.CompleteAsync(task, "intruso", now);
		}

		(await AcquireAsync(task, "b", TimeSpan.FromMinutes(30), now.AddMinutes(1))).Should().BeFalse();
	}

	[Fact]
	public async Task Corrida_NaPrimeiraTomada_TemUmaSoVencedora()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;

		var results = await Task.WhenAll(Enumerable.Range(0, 10)
			.Select(i => Task.Run(() => AcquireAsync(task, $"inst-{i}", TimeSpan.FromMinutes(30), now))));

		results.Count(won => won).Should().Be(1);
	}

	[Fact]
	public async Task Corrida_NaTomadaDeLeaseExpirado_TemUmaSoVencedora()
	{
		var task = Task_();
		var now = DateTimeOffset.UtcNow;
		await AcquireAsync(task, "antiga", TimeSpan.FromMinutes(1), now);

		var results = await Task.WhenAll(Enumerable.Range(0, 10)
			.Select(i => Task.Run(() => AcquireAsync(task, $"inst-{i}", TimeSpan.FromMinutes(30), now.AddHours(1)))));

		results.Count(won => won).Should().Be(1);
	}

	[Fact]
	public async Task TarefasDiferentes_TemLeasesIndependentes()
	{
		var now = DateTimeOffset.UtcNow;

		(await AcquireAsync(Task_(), "a", TimeSpan.FromMinutes(30), now)).Should().BeTrue();
		(await AcquireAsync(Task_(), "a", TimeSpan.FromMinutes(30), now)).Should().BeTrue();
	}
}

/// <summary>O contrato do lease no SQL Server (banco de plataforma da suíte).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public sealed class SqlServerBackgroundLeaseStoreTests(SecureGateApiFactory factory) : BackgroundLeaseStoreContract, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	protected override (IBackgroundLeaseStore Store, IDisposable Handle) NewStore()
	{
		var scope = factory.Services.CreateScope();

		return (scope.ServiceProvider.GetRequiredService<IBackgroundLeaseStore>(), scope);
	}
}

/// <summary>O MESMO contrato do lease no PostgreSQL real (ADR-0018): a atomicidade é do banco, nos dois engines.</summary>
public sealed class PostgresBackgroundLeaseStoreTests(PostgresRlsFixture fixture)
	: BackgroundLeaseStoreContract, IClassFixture<PostgresRlsFixture>
{
	protected override (IBackgroundLeaseStore Store, IDisposable Handle) NewStore()
	{
		// Papel de runtime (sem posse), sem escopo de tenant: o lease é dado de plataforma, fora do RLS
		var context = PostgresRlsFixture.ContextFor(fixture.RuntimeConnection(), _ => { });

		return (new BackgroundLeaseStore(context), context);
	}
}
