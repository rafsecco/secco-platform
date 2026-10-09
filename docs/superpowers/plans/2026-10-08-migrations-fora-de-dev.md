# Migrations e seed de referência fora de Development — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Verbo `migrate` no binário de cada produto (migrations + seed de referência), migração do banco de tenant no primeiro uso, e recusa do SecureGate em subir com migration pendente fora de DEV (issue #34, ADR-0038).

**Architecture:** O SDK EF Core ganha a porta `ISeccoDatabaseMigrator`, a rotina `RunSeccoMigrationsAsync` (migrators → seed), o reconhecedor de verbo `SeccoCommands.IsMigrate` e um `DbConnectionInterceptor` que, via um gate singleton, migra cada banco de tenant na primeira abertura. O SharedKernel ganha `SeccoTransientException` para o `503` ser compartilhado entre os dois pacotes do SDK. Cada produto implementa o seu migrator e troca o bloco `IsDevelopment()` do `Program.cs`.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server + PostgreSQL), ASP.NET Core minimal hosting, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers, Docker Compose.

**Spec:** `docs/superpowers/specs/2026-10-08-migrations-fora-de-dev-design.md` (e a ADR-0038 em `docs/adr/secco-platform-adrs.md`).

## Global Constraints

- Nunca contradizer ADR Aceita. ADR-0038: seed de referência **só** no `migrate` e em Development, nunca no boot de produção.
- Connection string **nunca** em log, mensagem de exceção, resposta HTTP ou chave em memória (o gate guarda SHA-256 hex).
- `Secco.SDK.EntityFrameworkCore` **não** referencia `Secco.SDK.AspNetCore`, e vice-versa; o contrato comum é `Secco.SharedKernel.Exceptions.SeccoTransientException`.
- `SeccoTransientException` é abstrata, deriva de `SeccoException`, sem estado nem dependência (ADR-0003).
- `Retry-After` do `503`: `15` segundos (constante existente `SeccoTenancyExceptionMiddleware.RetryAfterSeconds`).
- Verbo: exatamente `migrate`, primeiro argumento, comparação ordinal.
- Código de saída do `migrate`: `0` sucesso total, `1` qualquer falha.
- Falha de um tenant não interrompe os demais; seed não roda se qualquer migrator falhar.
- Logs via `[LoggerMessage]` (CA1848 é erro no build Release dos projetos de produção).
- Build Release com warnings = erros: `dotnet build Secco.Platform.slnx --configuration Release`. Verificação final: `dotnet test Secco.Platform.slnx`.
- Commits Conventional Commits direto na `main`, corpo terminando com a linha `Co-Authored-By:` do modelo que escreveu.

## Review Focus

- **Host de teste em `Testing` sobe antes de migrar** (`SeccoApiFactory`): a checagem de pendência do SecureGate precisa ficar desligada nas factories existentes, senão toda a suíte do SecureGate quebra. Coberto pela configuração `SecureGate:Database:VerifyMigrationsOnStartup` (Task 4).
- **Conexão aberta pelo próprio contexto de migração** dentro do interceptor: sem a exclusão, recursão infinita. O contexto de migração é criado sem o interceptor (Task 3), e o teste de tenant novo (Task 5) só passa se não houver recursão.
- **Abertura síncrona de conexão** (`ConnectionOpening`) — o worker de ingestão ou código legado pode abrir síncrono; o interceptor cobre os dois (Task 3).
- **Tenant com banco já migrado** não pode pagar `MigrateAsync` a cada processo: só `GetPendingMigrationsAsync`, e só uma vez por processo (teste do gate, Task 3).
- **`migrate` sem tenant nenhum no catálogo** deve sair `0` (nada a fazer), não erro (Task 5).

---

### Task 1: `SeccoTransientException` e `503` genérico no middleware

**Files:**
- Create: `src/SharedKernel/Secco.SharedKernel/Exceptions/SeccoTransientException.cs`
- Modify: `src/SDK/Secco.SDK.AspNetCore/Tenancy/TenantCatalogUnavailableException.cs` (base passa a `SeccoTransientException`)
- Modify: `src/SDK/Secco.SDK.AspNetCore/Tenancy/SeccoTenancyExceptionMiddleware.cs`
- Test: `tests/SharedKernel/Secco.SharedKernel.Tests/Exceptions/SeccoExceptionTests.cs`, `tests/SDK/Secco.SDK.AspNetCore.Tests/Tenancy/SeccoTenancyExceptionMiddlewareTests.cs`

**Interfaces:**
- Produces: `public abstract class SeccoTransientException : SeccoException` com os três construtores protegidos (sem argumento, `message`, `message + innerException`).

- [ ] **Step 1: Failing tests.** Em `SeccoExceptionTests`, acrescentar:

```csharp
	private sealed class TestTransientException(string message) : SeccoTransientException(message);

	[Fact]
	public void SeccoTransientException_DerivaDeSeccoException() =>
		new TestTransientException("x").Should().BeAssignableTo<SeccoException>();
```

Em `SeccoTenancyExceptionMiddlewareTests`, seguindo o padrão do teste existente do catálogo indisponível, acrescentar um teste em que o `next` lança uma `SeccoTransientException` de teste (classe privada derivada) e esperar `503`, header `Retry-After: 15` e `application/problem+json`. E um teste que confirma `TenantCatalogUnavailableException` é `SeccoTransientException`.

- [ ] **Step 2: Run, see fail.** `dotnet test tests/SharedKernel/Secco.SharedKernel.Tests` e `dotnet test tests/SDK/Secco.SDK.AspNetCore.Tests --filter "FullyQualifiedName~SeccoTenancyExceptionMiddlewareTests"` → falha de compilação.

- [ ] **Step 3: Implement.**

`SeccoTransientException.cs`:

```csharp
namespace Secco.SharedKernel.Exceptions;

/// <summary>
/// Falha TRANSITÓRIA de infraestrutura: o chamador pode tentar de novo (ADR-0038). Base comum
/// para os pacotes do SDK, que não se referenciam, traduzirem a mesma condição em 503 +
/// Retry-After. A mensagem nunca carrega segredo nem connection string (ADR-0020).
/// </summary>
public abstract class SeccoTransientException : SeccoException
{
	/// <summary>Inicializa a exceção sem mensagem específica.</summary>
	protected SeccoTransientException()
	{
	}

	/// <summary>Inicializa a exceção com a mensagem informada.</summary>
	/// <param name="message">Mensagem descrevendo a falha.</param>
	protected SeccoTransientException(string message)
		: base(message)
	{
	}

	/// <summary>Inicializa a exceção com mensagem e exceção interna.</summary>
	/// <param name="message">Mensagem descrevendo a falha.</param>
	/// <param name="innerException">Exceção que causou esta.</param>
	protected SeccoTransientException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
```

`TenantCatalogUnavailableException`: trocar `: SeccoException` por `: SeccoTransientException` (o `using Secco.SharedKernel.Exceptions;` já existe ou acrescentar).

Middleware: substituir o `catch (TenantCatalogUnavailableException exception)` por:

```csharp
		catch (SeccoTransientException exception) when (!context.Response.HasStarted)
		{
			// Catálogo ou banco do tenant indisponível (ADR-0038): condição transitória — o
			// client com retry da plataforma se recupera sozinho. O detalhe é a mensagem fixa
			// da exceção, que nunca carrega connection string.
			TenancyLog.TransientFailure(logger, exception.GetType().Name, exception);

			context.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
			await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable,
				"Serviço temporariamente indisponível",
				exception.Message).ConfigureAwait(false);
		}
```

Em `TenancyLog.cs`, acrescentar `TransientFailure(ILogger logger, string exceptionType, Exception exception)` como `[LoggerMessage(Level = Warning, Message = "Falha transitória de tenancy ({ExceptionType}); respondendo 503.")]`, com EventId livre seguinte. Manter `CatalogUnavailable` se ainda for usado em outro lugar; se ficar sem uso, removê-lo. Atualizar o `<summary>` da classe do middleware.

Se o teste existente do catálogo verificar o título/detalhe antigo ("Catálogo de tenants indisponível"), ajustar a asserção para a mensagem da exceção (`"O catálogo de tenants da plataforma está temporariamente indisponível."`) — e registrar no relatório.

- [ ] **Step 4: Run, see pass** (mesmos comandos do Step 2).

- [ ] **Step 5: Commit** — `feat(sdk): SeccoTransientException e 503 generico no middleware de tenancy (ADR-0038)`.

---

### Task 2: porta `ISeccoDatabaseMigrator`, rotina e verbo

**Files:**
- Create: `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/ISeccoDatabaseMigrator.cs`
- Create: `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/SeccoMigrationExtensions.cs`
- Create: `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/SeccoCommands.cs`
- Test: `tests/SDK/Secco.SDK.EntityFrameworkCore.Tests/Migrations/SeccoMigrationExtensionsTests.cs`, `.../Migrations/SeccoCommandsTests.cs`

**Interfaces:**
- Produces:
  - `public interface ISeccoDatabaseMigrator { string Name { get; } Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default); }`
  - `public static Task<bool> RunSeccoMigrationsAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)`
  - `public static class SeccoCommands { public const string Migrate = "migrate"; public static bool IsMigrate(string[] args); }`

- [ ] **Step 1: Failing tests.**

`SeccoCommandsTests.cs`:

```csharp
using FluentAssertions;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Xunit;

namespace Secco.SDK.EntityFrameworkCore.Tests.Migrations;

public class SeccoCommandsTests
{
	[Fact]
	public void IsMigrate_PrimeiroArgumentoMigrate_True() => SeccoCommands.IsMigrate(["migrate"]).Should().BeTrue();

	[Theory]
	[InlineData()]
	[InlineData("Migrate")]
	[InlineData("--migrate")]
	[InlineData("x", "migrate")]
	public void IsMigrate_QualquerOutraForma_False(params string[] args) => SeccoCommands.IsMigrate(args).Should().BeFalse();
}
```

`SeccoMigrationExtensionsTests.cs` — usar o mesmo estilo de montagem de `ServiceCollection` do `Seeding/SeccoSeedingExtensionsTests.cs` (ler o arquivo e reaproveitar o helper de `IHostEnvironment`/configuração dele). Casos:

```csharp
	// Migrator e seeder de teste que registram a ordem em uma lista compartilhada
	private sealed class RecordingMigrator(List<string> log, IReadOnlyList<string> failures, bool @throw = false) : ISeccoDatabaseMigrator
	{
		public string Name => "teste";
		public Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
		{
			log.Add("migrate");
			return @throw ? throw new InvalidOperationException("boom") : Task.FromResult(failures);
		}
	}

	private sealed class RecordingSeeder(List<string> log) : IReferenceDataSeeder
	{
		public Task SeedAsync(CancellationToken cancellationToken = default) { log.Add("seed"); return Task.CompletedTask; }
	}

	[Fact] RunSeccoMigrationsAsync_TudoOk_MigraDepoisSemeiaETrue        // log == ["migrate","migrate","seed"] com dois migrators; resultado true
	[Fact] RunSeccoMigrationsAsync_MigratorComFalha_NaoSemeiaEFalse     // um migrator devolve ["tenant-x"]; log sem "seed"; false; o OUTRO migrator também rodou
	[Fact] RunSeccoMigrationsAsync_MigratorLanca_NaoSemeiaEFalse        // @throw = true; não propaga; false; sem "seed"
	[Fact] RunSeccoMigrationsAsync_SemMigrators_SemeiaETrue             // só o seeder: log == ["seed"]; true
```

(Escrever os quatro testes por extenso com Arrange/Act/Assert; os comentários acima são a especificação de cada um.)

- [ ] **Step 2: Run, see fail.** `dotnet test tests/SDK/Secco.SDK.EntityFrameworkCore.Tests --filter "FullyQualifiedName~Migrations"`.

- [ ] **Step 3: Implement.**

`ISeccoDatabaseMigrator.cs`:

```csharp
namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// Aplica as migrations de um produto (ADR-0038): o banco de plataforma, ou todos os tenants do
/// catálogo. Um alvo que falha não interrompe os demais.
/// </summary>
public interface ISeccoDatabaseMigrator
{
	/// <summary>Nome para log (ex.: "LogStream (tenants)"). Nunca contém connection string.</summary>
	string Name { get; }

	/// <summary>Aplica as migrations. Devolve os alvos que falharam (id do tenant ou nome do banco); vazio = sucesso.</summary>
	Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default);
}
```

`SeccoCommands.cs`:

```csharp
namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>Verbos de linha de comando dos binários da plataforma (ADR-0038).</summary>
public static class SeccoCommands
{
	/// <summary>Verbo que aplica migrations e seed de referência e sai, sem subir o servidor.</summary>
	public const string Migrate = "migrate";

	/// <summary>Indica se o processo foi chamado como <c>migrate</c> (primeiro argumento, exato).</summary>
	/// <param name="args">Argumentos do processo.</param>
	public static bool IsMigrate(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);
		return args.Length > 0 && string.Equals(args[0], Migrate, StringComparison.Ordinal);
	}
}
```

`SeccoMigrationExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// O "processo controlado" da ADR-0005, materializado pela ADR-0038: migrations de todos os
/// <see cref="ISeccoDatabaseMigrator"/> e, só se TODAS passarem, o seed de referência. Chamado
/// pelo verbo <c>migrate</c> e pelo startup em Development — o mesmo código nos dois caminhos.
/// </summary>
public static class SeccoMigrationExtensions
{
	/// <summary>Executa migrations e seed. Devolve <c>true</c> em sucesso total.</summary>
	/// <param name="serviceProvider">Raiz de serviços da aplicação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public static async Task<bool> RunSeccoMigrationsAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(SeccoMigrationExtensions))
			?? NullLogger.Instance;
		var failed = false;

		await using (var scope = serviceProvider.CreateAsyncScope())
		{
			foreach (var migrator in scope.ServiceProvider.GetServices<ISeccoDatabaseMigrator>())
			{
				try
				{
					var failures = await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);

					foreach (var target in failures)
					{
						MigrationLog.TargetFailed(logger, migrator.Name, target);
					}

					failed |= failures.Count > 0;
				}
				catch (Exception exception) when (exception is not OperationCanceledException)
				{
					MigrationLog.MigratorFailed(logger, migrator.Name, exception);
					failed = true;
				}
			}
		}

		if (failed)
		{
			// O seed de referência pode ser destrutivo (ADR-0037 remove clients): nunca sobre schema pela metade
			MigrationLog.SeedSkipped(logger);
			return false;
		}

		await serviceProvider.SeedSeccoDataAsync(cancellationToken).ConfigureAwait(false);
		return true;
	}
}

/// <summary>Mensagens de log das migrations (source generator — ADR-0008).</summary>
internal static partial class MigrationLog
{
	[LoggerMessage(EventId = 101, Level = LogLevel.Error, Message = "Migrations: {Migrator} falhou no alvo {Target}.")]
	public static partial void TargetFailed(ILogger logger, string migrator, string target);

	[LoggerMessage(EventId = 102, Level = LogLevel.Error, Message = "Migrations: {Migrator} falhou.")]
	public static partial void MigratorFailed(ILogger logger, string migrator, Exception exception);

	[LoggerMessage(EventId = 103, Level = LogLevel.Error, Message = "Seed de referência NÃO executado: houve falha de migration (ADR-0038).")]
	public static partial void SeedSkipped(ILogger logger);
}
```

Atenção: a mensagem da exceção logada em `MigratorFailed` vem do EF/ADO.NET e pode, em casos raros, citar o servidor — nunca a senha. Se o analisador reclamar de `catch (Exception)` (CA1031), suprimir localmente com justificativa (`#pragma warning disable CA1031 // falha de um migrator não pode derrubar os demais; logada e contada`), no padrão que o repositório já usar (procurar `CA1031` no código para copiar o formato).

- [ ] **Step 4: Run, see pass.**

- [ ] **Step 5: Commit** — `feat(sdk): porta de migrator, rotina RunSeccoMigrationsAsync e verbo migrate (ADR-0038)`.

---

### Task 3: migração do tenant no primeiro uso (gate + interceptor)

**Files:**
- Create: `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/TenantDatabaseUnavailableException.cs`
- Create: `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/SeccoTenantMigrationGate.cs`
- Create: `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/SeccoTenantMigrationInterceptor.cs`
- Create: `src/SDK/Secco.SDK.EntityFrameworkCore/Migrations/SeccoTenantMigrationServiceCollectionExtensions.cs`
- Test: `tests/SDK/Secco.SDK.EntityFrameworkCore.Tests/Migrations/SeccoTenantMigrationGateTests.cs`

**Interfaces:**
- Consumes: `SeccoTransientException` (Task 1).
- Produces:
  - `public sealed class TenantDatabaseUnavailableException : SeccoTransientException` (construtor `(Exception innerException)`; mensagem fixa).
  - `public sealed class SeccoTenantMigrationGate` com `Task EnsureMigratedAsync(string connectionString, Func<CancellationToken, Task> migrate, CancellationToken cancellationToken)` e `internal bool IsKnown(string connectionString)` (só para teste) e `internal IReadOnlyCollection<string> Keys` (só para teste).
  - `public sealed class SeccoTenantMigrationInterceptor<TContext> : DbConnectionInterceptor where TContext : DbContext`.
  - `public static IServiceCollection AddSeccoTenantMigrations<TContext>(this IServiceCollection services, Func<IServiceProvider, string, TContext> createMigrationContext) where TContext : DbContext` — a fábrica recebe o provider raiz para ler as options do produto (ex.: SQL Server × PostgreSQL).
  - Uso no produto: `options.AddInterceptors(serviceProvider.GetRequiredService<SeccoTenantMigrationInterceptor<TContext>>())`.

- [ ] **Step 1: Failing tests** — `SeccoTenantMigrationGateTests.cs`:

```csharp
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
		var gate = new SeccoTenantMigrationGate();
		var attempt = 0;
		Task Migrate(CancellationToken _) => ++attempt == 1 ? throw new InvalidOperationException("servidor fora") : Task.CompletedTask;

		var first = () => gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		var thrown = await first.Should().ThrowAsync<TenantDatabaseUnavailableException>();
		thrown.Which.InnerException.Should().BeOfType<InvalidOperationException>();
		thrown.Which.Message.Should().NotContain("segredo");

		await gate.EnsureMigratedAsync(ConnectionString, Migrate, CancellationToken.None);
		attempt.Should().Be(2, "falha não é memorizada");
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
```

O teste do projeto precisa enxergar `internal`: conferir se `src/SDK/Secco.SDK.EntityFrameworkCore/AssemblyInfo.cs` já tem `InternalsVisibleTo("Secco.SDK.EntityFrameworkCore.Tests")`; se não tiver, acrescentar.

- [ ] **Step 2: Run, see fail.**

- [ ] **Step 3: Implement.**

`TenantDatabaseUnavailableException.cs`:

```csharp
using Secco.SharedKernel.Exceptions;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// O banco do tenant não pôde ser preparado (ADR-0038) — transitória: o SDK responde 503 +
/// Retry-After, e a próxima abertura tenta de novo. Mensagem fixa: nunca a connection string.
/// </summary>
public sealed class TenantDatabaseUnavailableException : SeccoTransientException
{
	/// <summary>Inicializa a exceção a partir da falha original.</summary>
	/// <param name="innerException">Falha do EF/ADO.NET ao migrar.</param>
	public TenantDatabaseUnavailableException(Exception innerException)
		: base("O banco de dados do tenant está temporariamente indisponível.", innerException)
	{
	}
}
```

`SeccoTenantMigrationGate.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// Memória, por processo, dos bancos de tenant já conferidos (ADR-0038). Chave = SHA-256 da
/// connection string — o texto nunca fica guardado aqui. Aberturas concorrentes do mesmo banco
/// aguardam a mesma tarefa; o lock do EF cobre as outras réplicas. Falha não é memorizada.
/// </summary>
public sealed class SeccoTenantMigrationGate
{
	private readonly ConcurrentDictionary<string, Lazy<Task>> _known = new(StringComparer.Ordinal);

	internal IReadOnlyCollection<string> Keys => [.. _known.Keys];

	/// <summary>Garante que o banco foi conferido/migrado neste processo.</summary>
	/// <param name="connectionString">Connection string do banco do tenant.</param>
	/// <param name="migrate">Aplica as migrations pendentes (sem o interceptor).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task EnsureMigratedAsync(string connectionString, Func<CancellationToken, Task> migrate, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(connectionString);
		ArgumentNullException.ThrowIfNull(migrate);

		var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(connectionString)));
		// CancellationToken.None na tarefa compartilhada: o cancelamento de UMA requisição não pode
		// cancelar a migração que outras estão aguardando.
		var entry = _known.GetOrAdd(key, _ => new Lazy<Task>(() => migrate(CancellationToken.None)));

		try
		{
			await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_known.TryRemove(new KeyValuePair<string, Lazy<Task>>(key, entry));
			throw new TenantDatabaseUnavailableException(exception);
		}
	}
}
```

`SeccoTenantMigrationInterceptor.cs`:

```csharp
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>
/// Migra o banco do tenant na PRIMEIRA abertura de conexão no processo (ADR-0038): cobre HTTP,
/// workers e jobs, porque intercepta a conexão e não a requisição. Registrar só em contexto de
/// tenant. O contexto de migração é criado pela fábrica do produto, SEM este interceptor.
/// </summary>
/// <typeparam name="TContext">Contexto de tenant do produto.</typeparam>
public sealed class SeccoTenantMigrationInterceptor<TContext>(
	SeccoTenantMigrationGate gate,
	IServiceProvider serviceProvider,
	Func<IServiceProvider, string, TContext> createMigrationContext) : DbConnectionInterceptor
	where TContext : DbContext
{
	/// <inheritdoc />
	public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
		DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(connection);
		await gate.EnsureMigratedAsync(connection.ConnectionString, MigrateAsync(connection.ConnectionString), cancellationToken)
			.ConfigureAwait(false);
		return result;
	}

	/// <inheritdoc />
	public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
	{
		ArgumentNullException.ThrowIfNull(connection);
		gate.EnsureMigratedAsync(connection.ConnectionString, MigrateAsync(connection.ConnectionString), CancellationToken.None)
			.GetAwaiter().GetResult();
		return result;
	}

	private Func<CancellationToken, Task> MigrateAsync(string connectionString) => async cancellationToken =>
	{
		await using var context = createMigrationContext(serviceProvider, connectionString);

		if ((await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
		{
			await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
		}
	};
}
```

Atenção ao `ConnectionString` do `DbConnection`: em SQL Server com `Persist Security Info=False` (padrão), depois da abertura a senha some do `ConnectionString`. Aqui ainda é **antes** da abertura (`ConnectionOpening`), então o valor é o original — e a mesma string sempre gera a mesma chave. Não trocar para `ConnectionOpened`.

`SeccoTenantMigrationServiceCollectionExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Secco.SDK.EntityFrameworkCore.Migrations;

/// <summary>Registro da migração do tenant no primeiro uso (ADR-0038).</summary>
public static class SeccoTenantMigrationServiceCollectionExtensions
{
	/// <summary>
	/// Registra o gate (singleton, compartilhado) e o interceptor do contexto. O produto acrescenta
	/// o interceptor no seu <c>AddDbContext</c>. A fábrica cria um contexto SEM o interceptor.
	/// </summary>
	public static IServiceCollection AddSeccoTenantMigrations<TContext>(
		this IServiceCollection services, Func<IServiceProvider, string, TContext> createMigrationContext)
		where TContext : DbContext
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(createMigrationContext);

		services.TryAddSingleton<SeccoTenantMigrationGate>();
		services.TryAddSingleton(serviceProvider => new SeccoTenantMigrationInterceptor<TContext>(
			serviceProvider.GetRequiredService<SeccoTenantMigrationGate>(), serviceProvider, createMigrationContext));

		return services;
	}
}
```

- [ ] **Step 4: Run, see pass.** `dotnet test tests/SDK/Secco.SDK.EntityFrameworkCore.Tests` (projeto inteiro).

- [ ] **Step 5: Build Release do SDK EF Core** sem warnings: `dotnet build src/SDK/Secco.SDK.EntityFrameworkCore/Secco.SDK.EntityFrameworkCore.csproj --configuration Release`.

- [ ] **Step 6: Commit** — `feat(sdk): migracao do banco de tenant no primeiro uso por interceptor de conexao (ADR-0038)`.

---

### Task 4: SecureGate — migrator, `Program.cs` e recusa com migration pendente

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/SecureGatePlatformMigrator.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/SecureGateInfrastructureExtensions.cs` (registro + `EnsureSecureGateDatabaseMigratedAsync`)
- Modify: `src/SecureGate/Secco.SecureGate.Api/Program.cs`
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SecureGateApiFactory.cs` (desliga a checagem)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SecureGateMigrationTests.cs`

**Interfaces:**
- Consumes: `ISeccoDatabaseMigrator`, `RunSeccoMigrationsAsync`, `SeccoCommands` (Task 2).
- Produces: configuração `SecureGate:Database:VerifyMigrationsOnStartup` (bool, padrão `true`); `public static Task EnsureSecureGateDatabaseMigratedAsync(this IServiceProvider, CancellationToken = default)` que lança `InvalidOperationException` com a mensagem `"O banco de plataforma do SecureGate tem migrations pendentes. Rode 'dotnet Secco.SecureGate.Api.dll migrate' antes de subir (ADR-0038)."`.

- [ ] **Step 1: Failing tests** — `SecureGateMigrationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Secco.SDK.EntityFrameworkCore.Migrations;
using Secco.SecureGate.Infrastructure;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Comando migrate e recusa de startup com migration pendente (ADR-0038).</summary>
public class SecureGateMigrationTests
{
	private sealed class VerifyingFactory : SecureGateApiFactory
	{
		protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
		{
			base.ConfigureTestConfiguration(settings);
			settings["SecureGate:Database:VerifyMigrationsOnStartup"] = "true";
		}
	}

	[Fact]
	public async Task Startup_ForaDeDevComMigrationPendente_RecusaSubir()
	{
		await using var factory = new VerifyingFactory();
		await ((IAsyncLifetime)factory).InitializeAsync();   // sobe o SQL Server; NÃO migra

		var act = () => factory.CreateClient();

		act.Should().Throw<InvalidOperationException>().WithMessage("*migrate*");
		await ((IAsyncLifetime)factory).DisposeAsync();
	}

	[Fact]
	public async Task RunSeccoMigrations_BancoVazio_MigraESemeiaETrue()
	{
		await using var factory = new SecureGateApiFactory();
		await ((IAsyncLifetime)factory).InitializeAsync();
		_ = factory.CreateClient();   // constrói o host (checagem desligada na factory base)

		var ok = await factory.Services.RunSeccoMigrationsAsync();

		ok.Should().BeTrue();
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<Secco.SecureGate.Infrastructure.Contexts.SecureGateDbContext>();
		(await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
		(await context.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreScope<Guid>>().AnyAsync())
			.Should().BeTrue("o seed de referência registrou os escopos");
		await ((IAsyncLifetime)factory).DisposeAsync();
	}
}
```

Ajustar ao modo real como a `SeccoApiFactory` inicializa (ver `src/SDK/Secco.SDK.Testing/SeccoApiFactory.cs` e o teste `Startup_ClientIdComPrefixoDaApi_Falha` em `PlatformClientReconciliationTests.cs`, que já resolveu este mesmo problema de inicialização); a consulta de escopos pode usar `IOpenIddictScopeManager.CountAsync()` se for mais simples.

- [ ] **Step 2: Run, see fail.**

- [ ] **Step 3: Implement.**

`SecureGatePlatformMigrator.cs`:

```csharp
using Secco.SDK.EntityFrameworkCore.Migrations;

namespace Secco.SecureGate.Infrastructure;

/// <summary>Migrations do banco de PLATAFORMA do SecureGate (ADR-0038).</summary>
internal sealed class SecureGatePlatformMigrator(IServiceProvider serviceProvider) : ISeccoDatabaseMigrator
{
	public string Name => "SecureGate (plataforma)";

	public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
	{
		await serviceProvider.MigrateSecureGateDatabaseAsync(cancellationToken).ConfigureAwait(false);
		return [];
	}
}
```

(Exceção aqui sobe e é contada como falha pelo `RunSeccoMigrationsAsync` — banco único, não há "outros alvos".)

Em `AddSecureGateInfrastructure`: `services.AddScoped<ISeccoDatabaseMigrator, SecureGatePlatformMigrator>();` com comentário `// Processo controlado (ADR-0038): migrations do banco de plataforma`.

Em `SecureGateInfrastructureExtensions`, novo método:

```csharp
	/// <summary>
	/// Recusa subir com migration pendente no banco de plataforma (ADR-0038). Só leitura. O seed de
	/// referência não roda no boot de produção (sem lock, e remove clients — ADR-0037).
	/// </summary>
	public static async Task EnsureSecureGateDatabaseMigratedAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);

		using var scope = serviceProvider.CreateScope();
		var databaseOptions = scope.ServiceProvider.GetRequiredService<SecureGateDatabaseOptions>();
		var options = SecureGateDatabaseProviderConfigurator.CreateOptions(databaseOptions.Provider, databaseOptions.ConnectionString!);

		await using var context = new SecureGateDbContext(options);

		if ((await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any())
		{
			throw new InvalidOperationException(
				"O banco de plataforma do SecureGate tem migrations pendentes. Rode 'dotnet Secco.SecureGate.Api.dll migrate' antes de subir (ADR-0038).");
		}
	}
```

`Program.cs` — substituir o bloco final:

```csharp
// Processo controlado (ADR-0005/0038): `dotnet Secco.SecureGate.Api.dll migrate` aplica migrations
// e seed de referência e sai — o deploy o executa antes das réplicas.
if (SeccoCommands.IsMigrate(args))
{
	return await app.Services.RunSeccoMigrationsAsync() ? 0 : 1;
}

if (app.Environment.IsDevelopment())
{
	// UI de documentação (ADR-0006) — apenas em DEV
	app.MapScalarApiReference().AllowAnonymous();

	// Em DEV, o mesmo código do comando: F5 continua automático
	await app.Services.RunSeccoMigrationsAsync();
}
else if (app.Configuration.GetValue("SecureGate:Database:VerifyMigrationsOnStartup", true))
{
	// Fora de DEV: só confere — quem migra é o `migrate` do deploy
	await app.Services.EnsureSecureGateDatabaseMigratedAsync();
}

await app.RunAsync();
return 0;
```

Com `return` no top-level, o `Program` passa a devolver `int` — conferir que `public partial class Program;` continua compilando e que o `WebApplicationFactory` segue funcionando (ele intercepta antes do `RunAsync`). Acrescentar `using Secco.SDK.EntityFrameworkCore.Migrations;`.

Em `SecureGateApiFactory.ConfigureTestConfiguration` (criar o override se não existir, chamando `base`): `settings["SecureGate:Database:VerifyMigrationsOnStartup"] = "false";` com comentário: o host de teste sobe ANTES de a factory migrar o banco (ADR-0027); a checagem tem teste próprio em `SecureGateMigrationTests`.

- [ ] **Step 4: Run.** `dotnet test tests/SecureGate/Secco.SecureGate.Tests` — inteiro (a checagem não pode quebrar nenhuma factory existente).

- [ ] **Step 5: Commit** — `feat(securegate): comando migrate e recusa de startup com migration pendente (ADR-0038)`.

---

### Task 5: LogStream e NotificationHub — migrators por tenant, interceptor e `Program.cs`

**Files:**
- Create: `src/LogStream/Secco.LogStream.Infrastructure/LogStreamTenantMigrator.cs`
- Create: `src/NotificationHub/Secco.NotificationHub.Infrastructure/NotificationHubTenantMigrator.cs`
- Modify: `src/LogStream/Secco.LogStream.Infrastructure/LogStreamInfrastructureExtensions.cs`
- Modify: `src/NotificationHub/Secco.NotificationHub.Infrastructure/NotificationHubInfrastructureExtensions.cs`
- Modify: `src/LogStream/Secco.LogStream.Api/Program.cs`, `src/NotificationHub/Secco.NotificationHub.Api/Program.cs`
- Test: `tests/LogStream/Secco.LogStream.Tests/Integration/LazyTenantMigrationTests.cs`, `tests/NotificationHub/Secco.NotificationHub.Tests/Integration/LazyTenantMigrationTests.cs`

**Interfaces:**
- Consumes: Tasks 2 e 3.
- Produces: `LogStreamTenantMigrator`, `NotificationHubTenantMigrator` (`ISeccoDatabaseMigrator`, alvos = `TenantInfo.TenantId.ToString()`).

- [ ] **Step 1: Failing test (LogStream)** — `LazyTenantMigrationTests.cs`, com uma factory própria (a `LogStreamApiFactory` é `sealed`):

```csharp
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Secco.SDK.Testing;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

/// <summary>
/// Tenant provisionado DEPOIS do deploy (ADR-0028): banco vazio, ninguém rodou migrate — o primeiro
/// uso cria o schema, sem reiniciar o produto (ADR-0038).
/// </summary>
public sealed class LazyTenantFactory : SeccoApiFactory<Program>
{
	public const string DatabaseName = "secco_logstream_lazy";

	protected override string Audience => "secco-logstream";

	public Guid TenantNovo { get; } = Guid.NewGuid();

	// De propósito: NENHUMA migração prévia — é o cenário do tenant novo
	protected override Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;

	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		AddTenant(settings, TenantNovo, GetConnectionStringFor(DatabaseName));
		AddRolePermissions(settings, DefaultTestRole, "log-entries:read", "log-entries:write");
	}

	// Banco existente e vazio — como o provisionamento da ADR-0028 entrega
	protected override Task OnInitializedAsync() => CreateDatabaseAsync(DatabaseName);
}

public class LazyTenantMigrationTests(LazyTenantFactory factory) : IClassFixture<LazyTenantFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	[Fact]
	public async Task PrimeiroUso_TenantComBancoVazio_CriaSchemaEResponde()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(factory.TenantNovo));

		var response = await client.GetAsync("/api/v1/log-entries");

		response.StatusCode.Should().Be(HttpStatusCode.OK);

		await using var connection = new SqlConnection(factory.GetConnectionStringFor(LazyTenantFactory.DatabaseName));
		await connection.OpenAsync();
		await using var command = new SqlCommand("SELECT COUNT(*) FROM __EFMigrationsHistory", connection);
		((int)(await command.ExecuteScalarAsync())!).Should().BeGreaterThan(0);
	}
}
```

Ajustar: nome da tabela de histórico (conferir no `LogStreamDbContext`/configurator se foi renomeada pela convention, ex. `__ef_migrations_history`), o método de criar banco (`CreateDatabaseAsync` existe na base — ver `NotificationHubApiFactory.OnInitializedAsync`), e a rota de leitura (conferir em `LogEntryEndpointsTests`). Se `GetConnectionStringFor` não for público, usar o caminho que `LogStreamApiFactory`/`CrossProductTokenFlowTests` usam.

Acrescentar no mesmo arquivo de teste, para o Review Focus: `[Fact] MigrateCommand_SemTenantsNoCatalogo_True` não se aplica a esta factory (ela tem um tenant) — fazer em `LogStreamTenantMigrator` um teste unitário em `tests/LogStream/Secco.LogStream.Tests/Unit/` com `ITenantCatalog` substituto devolvendo lista vazia → `MigrateAsync` devolve vazio sem exceção.

- [ ] **Step 2: Run, see fail.** O `GET` lança/500 porque as tabelas não existem.

- [ ] **Step 3: Implement (LogStream).**

`LogStreamTenantMigrator.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.LogStream.Infrastructure.Contexts;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.EntityFrameworkCore.Migrations;

namespace Secco.LogStream.Infrastructure;

/// <summary>
/// Migrations de TODOS os tenants do catálogo (ADR-0038). Um tenant que falha não interrompe os
/// demais; o alvo devolvido é o id do tenant, nunca a connection string.
/// </summary>
internal sealed partial class LogStreamTenantMigrator(
	ITenantCatalog catalog,
	IOptions<LogStreamDatabaseOptions> databaseOptions,
	ILogger<LogStreamTenantMigrator> logger) : ISeccoDatabaseMigrator
{
	public string Name => "LogStream (tenants)";

	public async Task<IReadOnlyList<string>> MigrateAsync(CancellationToken cancellationToken = default)
	{
		var failures = new List<string>();

		foreach (var tenant in await catalog.ListAsync(cancellationToken).ConfigureAwait(false))
		{
			try
			{
				var options = LogStreamDatabaseProviderConfigurator.CreateOptions(
					databaseOptions.Value.Provider, tenant.ConnectionString);

				await using var context = new LogStreamDbContext(options);
				await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				LogTenantFailed(logger, tenant.TenantId, exception.Message);
				failures.Add(tenant.TenantId.ToString());
			}
		}

		return failures;
	}

	[LoggerMessage(Level = LogLevel.Error, Message = "Migrations do LogStream falharam no tenant {TenantId}: {Reason}")]
	private static partial void LogTenantFailed(ILogger logger, Guid tenantId, string reason);
}
```

(Conferir o nome real das propriedades de `TenantInfo` — `TenantId`/`ConnectionString` — em `src/SDK/Secco.SDK.AspNetCore/Tenancy/TenantInfo.cs`. Mesma observação de CA1031 da Task 2.)

`MigrateLogStreamTenantDatabasesAsync` (extensão existente, usada pelas factories de teste) passa a delegar ao migrator e lançar se houver falha — assim os testes que já a usam continuam iguais:

```csharp
		using var scope = serviceProvider.CreateScope();
		var migrator = ActivatorUtilities.CreateInstance<LogStreamTenantMigrator>(scope.ServiceProvider);
		var failures = await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);

		if (failures.Count > 0)
		{
			throw new InvalidOperationException($"Migrations do LogStream falharam em {failures.Count} tenant(s): {string.Join(", ", failures)}.");
		}
```

Em `AddLogStreamInfrastructure`:

```csharp
		// Processo controlado (ADR-0038) e migração do tenant novo no primeiro uso
		services.AddScoped<ISeccoDatabaseMigrator, LogStreamTenantMigrator>();
		services.AddSeccoTenantMigrations((serviceProvider, connectionString) =>
			// Sem interceptor: este contexto só serve para migrar
			new LogStreamDbContext(LogStreamDatabaseProviderConfigurator.CreateOptions(
				serviceProvider.GetRequiredService<IOptions<LogStreamDatabaseOptions>>().Value.Provider,
				connectionString)));
```

No `AddDbContext<LogStreamDbContext>`, depois do `Configure(...)`: `options.AddInterceptors(serviceProvider.GetRequiredService<SeccoTenantMigrationInterceptor<LogStreamDbContext>>());`.

`Program.cs` do LogStream — mesmo padrão da Task 4, **sem** o `else if` de checagem:

```csharp
if (SeccoCommands.IsMigrate(args))
{
	return await app.Services.RunSeccoMigrationsAsync() ? 0 : 1;
}

if (app.Environment.IsDevelopment())
{
	app.MapScalarApiReference().AllowAnonymous();
	await app.Services.RunSeccoMigrationsAsync();
}

await app.RunAsync();
return 0;
```

- [ ] **Step 4: Run (LogStream).** `dotnet test tests/LogStream/Secco.LogStream.Tests` inteiro → PASS.

- [ ] **Step 5: NotificationHub** — repetir Steps 1–4 com `NotificationHubTenantMigrator`, `NotificationHubDbContext`, `NotificationHubDatabaseProviderConfigurator`, `MigrateNotificationHubTenantDatabasesAsync`. Teste de tenant novo: `GET /api/v1/in-app-notifications?userId={Guid.NewGuid()}` com token do tenant → `200`. A factory de teste do NotificationHub precisa do banco de plataforma do Hangfire e da configuração de e-mail — copiar do `NotificationHubApiFactory`. O Hangfire **não** recebe interceptor.

- [ ] **Step 6: Commit** — `feat(logstream,notificationhub): comando migrate e migracao do tenant no primeiro uso (ADR-0038)`.

---

### Task 6: Template `secco-service`

**Files:**
- Create: `templates/secco-service/Secco.SampleService.Infrastructure/SampleServiceTenantMigrator.cs`
- Modify: `templates/secco-service/Secco.SampleService.Infrastructure/SampleServiceInfrastructureExtensions.cs`
- Modify: `templates/secco-service/Secco.SampleService.Api/Program.cs`

- [ ] **Step 1:** aplicar exatamente o padrão da Task 5 (LogStream) ao template, trocando os nomes (`SampleService*`).
- [ ] **Step 2: Validar o template** com o roteiro de `docs/testing-guide.md` (seção do template: `dotnet new install`, instanciar em `src/TemplateSmoke`, `dotnet ef migrations add` nos dois engines, testes, build do client). Esperado: testes verdes. **Apagar `src/TemplateSmoke` e desinstalar o template** ao final — o produto gerado nunca é commitado.
- [ ] **Step 3: Commit** — `feat(templates): produto gerado nasce com o comando migrate e a migracao no primeiro uso (ADR-0038)`.

---

### Task 7: Docker Compose — serviço `*-migrate` por produto

**Files:**
- Modify: `docker-compose.yml`
- Modify: `docs/testing-guide.md` (seção do compose)

- [ ] **Step 1:** Para cada produto (`securegate`, `logstream`, `notificationhub`), extrair `build`, `depends_on` e `environment` da API para uma âncora (`x-securegate: &securegate-base` etc.), criar o serviço:

```yaml
  securegate-migrate:
    <<: *securegate-base
    profiles: ["securegate", "all"]
    command: ["migrate"]          # vira argumento do ENTRYPOINT ["dotnet", "Secco.SecureGate.Api.dll"]
    restart: "no"
```

e na API acrescentar em `depends_on`:

```yaml
      securegate-migrate:
        condition: service_completed_successfully
```

Manter portas (`ports`) só na API. No `logstream`/`notificationhub`, se a API depende de `securegate-api` (federado), o `*-migrate` **não** depende dela, a menos que o migrator precise do catálogo remoto — conferir o `environment` de cada um: se o catálogo é por configuração no compose, não há dependência.

- [ ] **Step 2: Verificar** — `docker compose --profile all up -d --build`; `docker compose --profile all ps -a` mostra os três `*-migrate` com `Exited (0)` e as APIs `running`; `curl` em `/health` de cada API responde. Depois `docker compose --profile all down -v`.
- [ ] **Step 3:** `docs/testing-guide.md`: explicar o serviço `*-migrate` (o caminho de produção exercitado a cada `up`).
- [ ] **Step 4: Commit** — `build(compose): servico de migrate por produto antes da API (ADR-0038)`.

---

### Task 8: Prova por mutação

Sem commit de código de produção. Para cada linha: aplicar, rodar o filtro, confirmar FAIL, reverter (`git checkout -- <arquivo>`), confirmar PASS. Mutação que não derruba teste → fortalecer o TESTE (commit `test(...)`), nunca afrouxar.

| # | Invariante | Mutação | Teste que precisa falhar |
| --- | --- | --- | --- |
| 1 | seed não roda após falha | em `RunSeccoMigrationsAsync`, remover o `if (failed) return false;` | `RunSeccoMigrationsAsync_MigratorComFalha_NaoSemeiaEFalse` |
| 2 | falha não memorizada | no gate, remover o `TryRemove` | `EnsureMigrated_Falha_LancaTransitoriaEPermiteNovaTentativa` |
| 3 | chave sem segredo | no gate, usar `connectionString` como chave | `EnsureMigrated_ChaveGuardada_NaoContemAConnectionString` |
| 4 | interceptor no contexto de tenant | remover `AddInterceptors` do LogStream | `PrimeiroUso_TenantComBancoVazio_CriaSchemaEResponde` (LogStream) |
| 5 | idem NotificationHub | remover `AddInterceptors` do NotificationHub | teste de tenant novo do NotificationHub |
| 6 | recusa com pendência | remover o `else if` do SecureGate | `Startup_ForaDeDevComMigrationPendente_RecusaSubir` |
| 7 | 503 genérico | middleware volta a capturar só `TenantCatalogUnavailableException` | teste do middleware com `SeccoTransientException` de teste |
| 8 | uma migração por concorrência | no gate, trocar `GetOrAdd` por criar sempre uma nova tarefa | `EnsureMigrated_ChamadasConcorrentes_MigraUmaVez` |
| 9 | verbo exato | `IsMigrate` com `OrdinalIgnoreCase` | `IsMigrate_QualquerOutraForma_False("Migrate")` |

---

### Task 9: Documentação, verificação e fechamento

**Files:** `docs/getting-started.md`, `CHANGELOG.md`, `docs/roadmap.md`, `CLAUDE.md`, `docs/design-decisions-log.md`.

- [ ] **Step 1: getting-started** — seção "7. Implantar": o deploy roda `dotnet Secco.<Produto>.Api.dll migrate` (mesma imagem, mesma configuração) antes das réplicas; exemplo de passo de pipeline e de *init container* (Kubernetes `initContainers` com `args: ["migrate"]`); código de saída; tenant novo migra no primeiro uso; SecureGate recusa subir com migration pendente; `SecureGate:Database:VerifyMigrationsOnStartup` existe para testes e não deve ser desligado em produção.
- [ ] **Step 2: CHANGELOG** em "Não publicado": `Secco.SharedKernel` (`SeccoTransientException`), `Secco.SDK.AspNetCore` (middleware traduz qualquer `SeccoTransientException` em 503; título do ProblemDetails do catálogo mudou para "Serviço temporariamente indisponível"), `Secco.SDK.EntityFrameworkCore` (porta, rotina, verbo, interceptor), `Secco.Templates`, e a nota de serviço: **o primeiro deploy do SecureGate depois desta versão exige rodar `migrate` antes, senão a API não sobe**.
- [ ] **Step 3: roadmap** — #34 **Entregue** com ADR-0038; a linha da #31 perde a frase "Em produção depende da #34"; entrada em "Incrementos pós-Fase 8".
- [ ] **Step 4: CLAUDE.md** — estado: ADR-0038, e retirar a menção "fora de DEV depende da #34" da frase da ADR-0037.
- [ ] **Step 5: design-decisions-log** — as três perguntas da conversa, a emenda de testes (`VerifyMigrationsOnStartup`), e o resultado da mutação.
- [ ] **Step 6: Verificação** — `dotnet build Secco.Platform.slnx --configuration Release` e `dotnet test Secco.Platform.slnx`; anotar contagem.
- [ ] **Step 7: Commit** — `docs: fecha a entrega de migrations e seed fora de Development (issue #34, ADR-0038)`.
- [ ] **Step 8: Publicação** — não taguear sem confirmação do usuário; seguir `secco-platform-release` (SharedKernel, SDK AspNetCore, SDK EF Core e Templates em minor, mais a cadeia que o script pedir); fechar a #34 e comentar na #31.
