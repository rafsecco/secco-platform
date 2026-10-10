# Catálogo de permissões publicado por produto — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada produto publica o catálogo das suas permissões no SecureGate durante o `migrate`; o `SetRolePermissions` recusa permissão acrescentada fora de qualquer catálogo; órfãs ficam visíveis e podem ser removidas por tenant; o AdminPortal usa o catálogo (issue #33, ADR-0039).

**Architecture:** Tabela de plataforma `tb_permission_catalog_entries` no SecureGate, com `PUT` por produto sob escopo `permissions:<produto>` e leitura sob `securegate:admin`. O `Secco.SecureGate.Client` entrega o publicador; o SDK EF Core entrega um seeder de referência por delegate; cada produto liga os dois na Infrastructure. `SeccoPermissionDefinition` no SharedKernel é o tipo da declaração.

**Tech Stack:** .NET 10, EF Core 10 (SQL Server + PostgreSQL), OpenIddict, ASP.NET Core minimal APIs, Blazor Server (AdminPortal), NSwag, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** `docs/superpowers/specs/2026-10-10-catalogo-de-permissoes-design.md` (e a ADR-0039 em `docs/adr/secco-platform-adrs.md`).

## Global Constraints

- ADR-0039 é Aceita: publicar **nunca** toca perfil; a recusa vale só para permissão **acrescentada**; limpeza de órfãs só **por tenant**; colisão de nome entre produtos é **visível, não bloqueia**; sem namespace de produto no nome.
- Escopo de publicação: `permissions:<produto>`, comparado **ordinal** com o produto da rota. `permissions:*` nunca concedido a client de produto (já garantido por `SecureGateScopes.IsProductScope`, que não muda).
- Limites: produto kebab-case até 50 (mesma regra de `TenantDatabase.ProductMaxLength`); 0–200 entradas por produto; nome em `SeccoPermissions.IsValid`; descrição 1–200 após trim, sem caractere de controle; sem nome repetido; detalhe do erro de desconhecidas lista no máximo 20, ordenadas.
- `operationId` exatos: `PublishPermissionCatalog`, `ListPermissionCatalog`, `GetPermissionCatalog`, `RemoveUnknownRolePermissions`. Nenhum `operationId` existente muda.
- `RoleDetailDto` ganha `UnknownPermissions` (aditivo). `PermissionDefinitionDto` tem `AlsoDeclaredBy`.
- Banco por convention (ADR-0017); migration nos dois engines.
- Logs via `[LoggerMessage]`; nunca descrição inteira nem dados de perfil em log de colisão (só produto e nome).
- O `Secco.SecureGate.Client` **não** referencia `Secco.SDK.EntityFrameworkCore`.
- Build Release sem warnings: `dotnet build Secco.Platform.slnx --configuration Release`; verificação final `dotnet test Secco.Platform.slnx`.
- Commits Conventional Commits direto na `main`; corpo termina com a linha `Co-Authored-By:` do modelo que escreveu.

## Review Focus

- **Perfil já com permissão órfã recebendo `PUT` com ela + uma nova conhecida** deve passar (a órfã não é "acrescentada"). Teste na Task 4.
- **Caixa diferente** (`Log-Entries:Read`): `SeccoPermissions.IsValid` já recusa maiúscula? Conferir; se aceitar, a comparação com o catálogo é ordinal e o teste deve fixar o comportamento (Task 4).
- **Produto republicando sem uma permissão que ainda está em perfis** → aparece em `UnknownPermissions` e **não** sai do perfil (Task 3/4).
- **Limpeza de órfãs em tenant com perfil reservado** (`installation-operator` no tenant de plataforma) não toca o reservado (Task 5).
- **`migrate` de produto sem `Secco:SecureGate`** (compose, DEV isolado) não falha por causa do publicador (Task 7).

---

### Task 1: Tipos base — `SeccoPermissionDefinition` e seeder por delegate

**Files:**
- Create: `src/SharedKernel/Secco.SharedKernel/Authorization/SeccoPermissionDefinition.cs`
- Create: `src/SDK/Secco.SDK.EntityFrameworkCore/Seeding/SeccoSeedingServiceCollectionExtensions.cs`
- Test: `tests/SharedKernel/Secco.SharedKernel.Tests/Authorization/SeccoPermissionDefinitionTests.cs`, `tests/SDK/Secco.SDK.EntityFrameworkCore.Tests/Seeding/SeccoSeedingServiceCollectionExtensionsTests.cs`

**Interfaces — Produces:** `public sealed record SeccoPermissionDefinition(string Name, string Description);` e `public static IServiceCollection AddSeccoReferenceSeeder(this IServiceCollection services, Func<IServiceProvider, CancellationToken, Task> seed, int order = 0)`.

- [ ] **Step 1: Failing tests.** SharedKernel: igualdade por valor do record. SDK EF: registrar dois delegates com `order` 10 e 5 e um `IReferenceDataSeeder` comum com `Order` 0; `SeedSeccoDataAsync` executa na ordem 0, 5, 10 (lista compartilhada); o delegate recebe um `IServiceProvider` de escopo (resolve um serviço `Scoped` registrado no teste).
- [ ] **Step 2: Run, see fail.**
- [ ] **Step 3: Implement.**

```csharp
namespace Secco.SharedKernel.Authorization;

/// <summary>Permissão declarada por um produto, com a descrição mostrada a quem administra perfis (ADR-0039).</summary>
/// <param name="Name">Nome canônico <c>recurso:acao</c> (<see cref="SeccoPermissions"/>).</param>
/// <param name="Description">Descrição curta em linguagem de negócio.</param>
public sealed record SeccoPermissionDefinition(string Name, string Description);
```

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace Secco.SDK.EntityFrameworkCore.Seeding;

/// <summary>Registro de seeders de referência por delegate (ADR-0039).</summary>
public static class SeccoSeedingServiceCollectionExtensions
{
	/// <summary>
	/// Registra um <see cref="IReferenceDataSeeder"/> que executa o delegate no escopo do seed. Serve
	/// a pacotes que não referenciam este SDK (ex.: o client do SecureGate) — o produto liga os dois.
	/// </summary>
	public static IServiceCollection AddSeccoReferenceSeeder(
		this IServiceCollection services, Func<IServiceProvider, CancellationToken, Task> seed, int order = 0)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(seed);

		services.AddScoped<IReferenceDataSeeder>(serviceProvider => new DelegateReferenceDataSeeder(serviceProvider, seed, order));
		return services;
	}

	private sealed class DelegateReferenceDataSeeder(
		IServiceProvider serviceProvider, Func<IServiceProvider, CancellationToken, Task> seed, int order) : IReferenceDataSeeder
	{
		public int Order => order;

		public Task SeedAsync(CancellationToken cancellationToken = default) => seed(serviceProvider, cancellationToken);
	}
}
```

(O `IServiceProvider` injetado no factory do `AddScoped` já é o do escopo criado pelo `SeedSeccoDataAsync`.)

- [ ] **Step 4: Run, see pass.**
- [ ] **Step 5: Commit** — `feat(sdk): SeccoPermissionDefinition e seeder de referencia por delegate (ADR-0039)`.

---

### Task 2: SecureGate — entidade, migration e escopo `permissions:*`

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Domain/Permissions/PermissionCatalogEntry.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Contexts/SecureGateDbContext.cs` (DbSet + mapeamento)
- Create: migration `AddPermissionCatalog` nos dois engines
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateScopes.cs` (`PermissionsPrefix`, `PermissionsFor`)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Seeding/SecureGateReferenceDataSeeder.cs` (registra `permissions:logstream` e `permissions:notificationhub`, resource `secco-securegate`, como os `catalog:*`)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/PlatformSchemaTests.cs` (índice único) e teste de escopos registrados (onde os `catalog:*` já são conferidos, se houver; senão em `PlatformSchemaTests`)

**Interfaces — Produces:** `PermissionCatalogEntry(string product, string name, string description, DateTimeOffset publishedAt)` com `Id` (`Guid.CreateVersion7()`), `Product`, `Name`, `Description`, `PublishedAt`, `const int ProductMaxLength = 50`, `NameMaxLength = 200`, `DescriptionMaxLength = 200`, e `void UpdateDescription(string description, DateTimeOffset publishedAt)`. `SecureGateScopes.PermissionsPrefix = "permissions:"`, `PermissionsFor(string product)`.

- [ ] **Step 1: Failing test.** Inserir duas entradas `(logstream, log-entries:read)` → `DbUpdateException`; `(logstream, x:read)` e `(notificationhub, x:read)` → ok.
- [ ] **Step 2: Implement** entidade (construtor valida nulos com `ArgumentException`, regras de negócio ficam na Application), mapeamento (`HasIndex(e => new { e.Product, e.Name }).IsUnique()`, `HasMaxLength` nos três), DbSet `PermissionCatalogEntries`, escopos e seed de referência. Gerar migrations:

```bash
dotnet ef migrations add AddPermissionCatalog --project src/SecureGate/Secco.SecureGate.Migrations.SqlServer/Secco.SecureGate.Migrations.SqlServer.csproj --output-dir Migrations
dotnet ef migrations add AddPermissionCatalog --project src/SecureGate/Secco.SecureGate.Migrations.Postgres/Secco.SecureGate.Migrations.Postgres.csproj --output-dir Migrations
```

- [ ] **Step 3: Run** `PlatformSchemaTests` → PASS (inclui `Migrations_Always_ApplyFromScratch`).
- [ ] **Step 4: Commit** — `feat(securegate): tabela de catalogo de permissoes e escopo permissions por produto (ADR-0039)`.

---

### Task 3: SecureGate — publicar e ler o catálogo

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Permissions/IPermissionCatalogRepository.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Permissions/PermissionCatalogDtos.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Permissions/PublishPermissionCatalogHandler.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Permissions/ListPermissionCatalogHandler.cs` (lista e get)
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateErrors.cs` (classe `PermissionCatalog`)
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Permissions/PermissionCatalogRepository.cs`
- Create: `src/SecureGate/Secco.SecureGate.Api/Endpoints/PermissionCatalogEndpoints.cs`
- Create: `src/SecureGate/Secco.SecureGate.Api/Requests/PermissionCatalogRequests.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Authorization/ScopeAuthorization.cs` (`RequirePermissionsScopeAsync`)
- Modify: `Program.cs` (`app.MapPermissionCatalogEndpoints();`), DI da Application e da Infrastructure
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/PublishPermissionCatalogHandlerTests.cs`, `tests/SecureGate/Secco.SecureGate.Tests/Integration/PermissionCatalogApiTests.cs`

**Interfaces — Produces:**

```csharp
public sealed record PermissionDefinitionDto(string Name, string Description, IReadOnlyList<string> AlsoDeclaredBy);
public sealed record ProductPermissionCatalogDto(string Product, IReadOnlyList<PermissionDefinitionDto> Permissions, DateTimeOffset PublishedAt);
public sealed record PublishPermissionCatalogCommand(string? Product, IReadOnlyList<PermissionDefinitionInput>? Permissions);
public sealed record PermissionDefinitionInput(string? Name, string? Description);

public interface IPermissionCatalogRepository
{
    /// Substitui o conjunto do produto numa transação. Devolve os nomes que JÁ existem no catálogo de OUTROS produtos.
    Task<IReadOnlyList<string>> ReplaceAsync(string product, IReadOnlyList<SeccoPermissionDefinition> permissions, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductPermissionCatalogDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<ProductPermissionCatalogDto?> GetAsync(string product, CancellationToken cancellationToken = default);
    /// Dos nomes informados, devolve os que NENHUM produto declara.
    Task<IReadOnlyList<string>> FindUnknownAsync(IEnumerable<string> names, CancellationToken cancellationToken = default);
}
```

Erros (`SecureGateErrors.PermissionCatalog`): `ProductInvalid` (400), `TooManyPermissions` (400), `PermissionNameInvalid` (400), `DescriptionInvalid` (400), `DuplicateName` (400), `NotFound` (404).

- [ ] **Step 1: Failing unit tests** do handler (NSubstitute no repositório): cada regra de validação; lista vazia válida; colisão devolvida pelo repositório gera log `Warning` (verificar com logger de teste) e o resultado é sucesso.
- [ ] **Step 2: Failing integration tests** (`PermissionCatalogApiTests`, collection compartilhada; token por `factory.CreateTokenWithScopes(...)`):
  - `PUT /api/v1/permission-catalog/logstream` com `permissions:logstream` → `204`; `GET` (com `securegate:admin`) devolve as entradas;
  - republicar igual → `204` e `PublishedAt` inalterado;
  - republicar sem uma entrada → ela some do catálogo;
  - `PUT .../notificationhub` com `permissions:logstream` → `403`; sem escopo → `403`; `GET` sem `securegate:admin` → `403`;
  - colisão: `logstream` e `notificationhub` publicando `x:read` → ambos `204`, e `AlsoDeclaredBy` de cada um lista o outro;
  - `GET .../produto-que-nunca-publicou` → `404`.
- [ ] **Step 3: Implement.** Pontos obrigatórios:
  - `RequirePermissionsScopeAsync`: igual ao `RequireCatalogScopeAsync`, com `SecureGateScopes.PermissionsFor(product)` e **sem** `ToLowerInvariant` (a validação de kebab-case no handler recusa maiúscula de qualquer forma; comparação ordinal).
  - `ReplaceAsync`: carrega as entradas do produto; remove as ausentes; para as presentes com descrição igual não toca (nem `PublishedAt`); atualiza as com descrição diferente; insere as novas com `now`. `PublishedAt` do produto na leitura = máximo das entradas.
  - Handler: valida tudo antes do repositório; para cada nome em colisão devolvido, `LogNameCollision(logger, product, name)`.
- [ ] **Step 4: Run, see pass.**
- [ ] **Step 5: Commit** — `feat(securegate): publicacao e leitura do catalogo de permissoes por produto (ADR-0039)`.

---

### Task 4: SecureGate — recusa do acrescentado e `UnknownPermissions`

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Roles/SetRolePermissionsHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/Roles/RoleDetailDto.cs` e `GetRoleHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateErrors.cs` (`Roles.PermissionsNotInCatalog`)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/SetRolePermissionsCatalogTests.cs`, integração em `tests/SecureGate/Secco.SecureGate.Tests/Integration/RoleManagementTests.cs`

**Interfaces — Consumes:** `IPermissionCatalogRepository.FindUnknownAsync` (Task 3), `IRoleRepository.GetPermissionsAsync` (existente).

- [ ] **Step 1: Failing tests:**
  - perfil sem permissões + `PUT ["log-entries:read"]` com catálogo do LogStream publicado → `204`;
  - `PUT ["pedido:aprovra"]` → `400`, detalhe contém `pedido:aprovra`;
  - perfil já com órfã `antiga:read` + `PUT ["antiga:read", "log-entries:read"]` → `204` (Review Focus);
  - perfil com `log-entries:read`, catálogo republicado sem ela → `GET` do perfil traz `unknownPermissions: ["log-entries:read"]` e `permissions` ainda a contém;
  - 25 desconhecidas → detalhe lista 20.
  - **Atenção a testes existentes**: `RoleManagementTests` e outros gravam permissões arbitrárias (`documentos:read`, `boletos:read`) via API. Elas passam a exigir catálogo. Ajustar esses testes publicando um catálogo de teste antes (helper `IdentitySeed.PermissionCatalogAsync(factory, product, params string[] names)` que insere direto no banco) — **não** afrouxar a regra. Testes que gravam permissões direto no banco (`IdentitySeed.RoleAsync`) não mudam.
- [ ] **Step 2: Implement.** No handler, depois de validar formato e antes de `ReplacePermissionsAsync`:

```csharp
		var current = await repository.GetPermissionsAsync(command.TenantId, name, cancellationToken).ConfigureAwait(false);

		if (current is null)
		{
			return Result.Failure(await repository.TenantExistsAsync(command.TenantId, cancellationToken).ConfigureAwait(false)
				? SecureGateErrors.Roles.NotFound
				: SecureGateErrors.Tenants.NotFound);
		}

		// ADR-0039: só o ACRESCENTADO precisa estar em algum catálogo; órfã já gravada passa
		var added = normalized.Except(current, StringComparer.Ordinal).ToList();
		var unknown = added.Count == 0
			? []
			: await catalog.FindUnknownAsync(added, cancellationToken).ConfigureAwait(false);

		if (unknown.Count > 0)
		{
			return Result.Failure(SecureGateErrors.Roles.PermissionsNotInCatalog(unknown));
		}
```

`PermissionsNotInCatalog(IReadOnlyList<string> unknown)` é um método que monta o `Error.Validation` com até 20 nomes ordenados na mensagem (`SecureGate.Role.PermissionsNotInCatalog`). Conferir se `Error` aceita mensagem dinâmica (ver outros erros com interpolação em `SecureGateErrors`). `GetRoleHandler` preenche `UnknownPermissions` com `FindUnknownAsync(role.Permissions)`.
- [ ] **Step 3: Run** `tests/SecureGate/Secco.SecureGate.Tests` inteiro → PASS.
- [ ] **Step 4: Commit** — `feat(securegate): perfil recusa permissao acrescentada fora do catalogo e expoe orfas (ADR-0039)`.

---

### Task 5: SecureGate — remoção de órfãs por tenant

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Roles/RemoveUnknownRolePermissionsHandler.cs` (+ DTOs)
- Modify: `IRoleRepository`/`RoleRepository` (método que lista perfis não reservados do tenant com permissões e grava a remoção numa transação)
- Modify: `RoleEndpoints.cs` (`POST /api/v1/tenants/{tenantId:guid}/roles/remove-unknown-permissions`, `WithName("RemoveUnknownRolePermissions")`, `securegate:admin`)
- Test: integração em `RoleManagementTests` ou arquivo novo `RemoveUnknownPermissionsTests.cs`

**Interfaces — Produces:** `RemovedUnknownPermissionsDto(int RolesChanged, int PermissionsRemoved, IReadOnlyList<RoleUnknownPermissionsDto> Roles)`, `RoleUnknownPermissionsDto(string Role, IReadOnlyList<string> Removed)`.

- [ ] **Step 1: Failing tests:** dois tenants com perfis contendo órfãs → chamada no tenant A remove só de A, B intacto; segunda chamada → `RolesChanged == 0`; tenant de plataforma: o `installation-operator` (reservado) não é tocado; tenant inexistente → `404`; perfil sem órfã não aparece em `Roles`.
- [ ] **Step 2: Implement** (handler usa `FindUnknownAsync` sobre a união das permissões do tenant; pula `RoleInputRules.IsReservedName`).
- [ ] **Step 3: Run, see pass.**
- [ ] **Step 4: Commit** — `feat(securegate): remocao de permissoes orfas dos perfis de um tenant (ADR-0039)`.

---

### Task 6: Contrato, client e publicador

**Files:**
- Modify: snapshot `src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json` (via `SECCO_UPDATE_OPENAPI=true` no `OpenApiContractTests`)
- Create: `src/SecureGate/Secco.SecureGate.Client/Permissions/ISecureGatePermissionCatalogPublisher.cs`, `SecureGatePermissionCatalogPublisher.cs`, `SecureGatePermissionCatalogPublisherExtensions.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Client/README.md`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/SecureGatePermissionCatalogPublisherTests.cs`

**Interfaces — Produces:** `ISecureGatePermissionCatalogPublisher.PublishAsync(string product, IReadOnlyList<SeccoPermissionDefinition> permissions, CancellationToken)`; `AddSecureGatePermissionCatalogPublisher(this IServiceCollection)`.

- [ ] **Step 1:** regenerar o snapshot; conferir no diff que entraram só os 4 `operationId` novos, os DTOs novos e `unknownPermissions` no `RoleDetailDto`, e que nenhum `operationId` existente mudou.
- [ ] **Step 2: Failing unit tests** do publicador: sem a seção `Secco:SecureGate` (`SecureGateClientCredentialsOptions.IsConfigured == false`) não chama HTTP e loga; com a seção, envia `PUT /api/v1/permission-catalog/{product}` com o corpo esperado (usar `HttpMessageHandler` de teste como nos testes existentes do client); resposta `403`/`500` → lança.
- [ ] **Step 3: Implement** seguindo `SecureGateTenantCatalogExtensions`: `AddSecureGateClientCredentialsOptions()`, `HttpClient` nomeado próprio (`SecureGatePermissionCatalogPublisher.HttpClientName`), `SeccoClientCredentialsHandler` com escopo `"permissions:" + product` — como o escopo depende do produto e o handler é por client nomeado, criar o handler com o produto lido de `SecureGateClientCredentialsOptions.Product` **ou** criar o client gerado por chamada com um `HttpClient` montado no próprio publicador; escolher o mais simples que mantenha o token store fora do pipeline, e documentar. O `PublishAsync` valida `product` não vazio, cria o client NSwag gerado e chama `PublishPermissionCatalogAsync`. Log de pulo e de sucesso via `[LoggerMessage]`.
- [ ] **Step 4: Run** testes do client e build Release do `Secco.SecureGate.Client`.
- [ ] **Step 5: Commit** — `feat(securegate): contrato, client e publicador do catalogo de permissoes (ADR-0039)`.

---

### Task 7: Produtos — catálogo declarado, publicado no `migrate`, e DEV

**Files:**
- Modify: `src/LogStream/Secco.LogStream.Application/LogStreamPermissions.cs` (`Catalog`)
- Modify: `src/NotificationHub/Secco.NotificationHub.Application/NotificationHubPermissions.cs` (`Catalog`)
- Modify: `src/LogStream/Secco.LogStream.Infrastructure/LogStreamInfrastructureExtensions.cs` e `src/NotificationHub/Secco.NotificationHub.Infrastructure/NotificationHubInfrastructureExtensions.cs` (publicador + `AddSeccoReferenceSeeder`)
- Modify: `src/SecureGate/Secco.SecureGate.Api/appsettings.Development.json` (`secco-dev-console` ganha `permissions:logstream`, `permissions:notificationhub`)
- Test: `tests/LogStream/Secco.LogStream.Tests/Unit/PermissionCatalogCompletenessTests.cs`, idem NotificationHub; E2E em `tests/SecureGate/Secco.SecureGate.Tests/Integration/PermissionCatalogPublishE2ETests.cs`

- [ ] **Step 1: Failing reflection tests** (um por produto):

```csharp
	[Fact]
	public void Catalog_ContemTodaConstanteDePermissao()
	{
		var constants = typeof(LogStreamPermissions).GetNestedTypes()
			.SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
			.Where(field => field.IsLiteral && field.FieldType == typeof(string))
			.Select(field => (string)field.GetRawConstantValue()!)
			.ToHashSet(StringComparer.Ordinal);

		LogStreamPermissions.Catalog.Select(p => p.Name).Should().BeEquivalentTo(constants);
		LogStreamPermissions.Catalog.Should().OnlyContain(p => !string.IsNullOrWhiteSpace(p.Description));
	}
```

- [ ] **Step 2: Implement** os `Catalog` (descrições = os `<summary>` das constantes, sem ponto final) e a ligação:

```csharp
		// Catálogo de permissões publicado no migrate (ADR-0039); sem Secco:SecureGate, o publicador pula
		services.AddSecureGatePermissionCatalogPublisher();
		services.AddSeccoReferenceSeeder((serviceProvider, cancellationToken) =>
			serviceProvider.GetRequiredService<ISecureGatePermissionCatalogPublisher>()
				.PublishAsync("logstream", LogStreamPermissions.Catalog, cancellationToken));
```

Conferir que a Infrastructure de cada produto já referencia `Secco.SecureGate.Client` (o catálogo de tenants usa); se não, acrescentar `ProjectReference` — e, se isso mudar o Dockerfile (memória: ProjectReference entre produtos exige `COPY` no Dockerfile), ajustar o Dockerfile do produto.
- [ ] **Step 3: E2E** (`PermissionCatalogPublishE2ETests`, padrão do `CrossProductTokenFlowTests`): LogStream host com `Secco:SecureGate` apontando para o SecureGate de teste (handler in-memory), client de plataforma com `permissions:logstream` criado por `factory.CreateClientAsync`; rodar `logStreamHost.Services.SeedSeccoDataAsync()`; `GET /api/v1/permission-catalog/logstream` no SecureGate devolve todas as permissões do `LogStreamPermissions.Catalog`. E um teste sem a seção: `SeedSeccoDataAsync` não lança.
- [ ] **Step 4: Run** LogStream, NotificationHub e SecureGate inteiros → PASS.
- [ ] **Step 5: Commit** — `feat(logstream,notificationhub): catalogo de permissoes publicado no migrate (ADR-0039)`.

---

### Task 8: Template

- [ ] Aplicar o padrão da Task 7 ao `templates/secco-service` (`SampleServicePermissions.Catalog`, ligação na Infrastructure, teste de reflexão). Produto: o identificador que o template já usa (conferir no `appsettings`/catálogo do template; se não houver, `sample-service`, substituído pelo `sourceName` do template).
- [ ] Validar pelo roteiro de `docs/testing-guide.md`; apagar `src/TemplateSmoke` e desinstalar o template; `git status` limpo de artefatos gerados.
- [ ] Commit — `feat(templates): produto gerado declara e publica o catalogo de permissoes (ADR-0039)`.

---

### Task 9: AdminPortal

**Files:**
- Modify: `src/AdminPortal/Secco.AdminPortal/Components/Pages/RoleManagement.razor`
- Modify: `src/AdminPortal/Secco.AdminPortal/Components/Pages/TenantManagement.razor`
- Create: `src/AdminPortal/Secco.AdminPortal/Components/Pages/PermissionCatalog.razor` (`@page "/permission-catalog"`, gate `Operator` como as demais)
- Create/Modify: serviços em `src/AdminPortal/Secco.AdminPortal/Services/` (`IPermissionCatalogService` + implementação; `IRoleAdminService` ganha `RemoveUnknownPermissionsAsync`), registrados como os existentes, usando `ISecureGateClientFactory` (token do operador)
- Modify: navegação (menu) com o link do catálogo
- Test: `tests/AdminPortal/Secco.AdminPortal.Tests/PermissionCatalogServiceTests.cs` no padrão de `IdentityAdminServicesTests.cs`

- [ ] **Step 1: Failing service tests:** o serviço agrupa por produto, preserva descrição e `AlsoDeclaredBy`; `RemoveUnknownPermissionsAsync` chama o endpoint do tenant certo e devolve o resumo; erro do client vira mensagem como nos serviços existentes.
- [ ] **Step 2: Implement** a página de perfil: caixas por produto (marcadas = permissões atuais conhecidas), seção "Não declaradas por nenhum produto" com as `UnknownPermissions` marcadas e o aviso "não concede nada"; salvar envia o conjunto marcado (PUT completo). Aviso de colisão (texto "também declarada por: …") ao lado da permissão quando `AlsoDeclaredBy` não for vazio. Botão "Remover permissões órfãs" na página do tenant com confirmação (`confirm` via JS interop **ou** um passo de confirmação na própria página — seguir o que o AdminPortal já usa para ações destrutivas, ex.: desativar usuário). Página de catálogo: tabela por produto com nome, descrição, colisões e data.
- [ ] **Step 3: Run** `tests/AdminPortal/Secco.AdminPortal.Tests` → PASS; build Release.
- [ ] **Step 4: Verificação manual** (se o ambiente permitir): F5 federado (SecureGate + LogStream + AdminPortal), logar como operador demo, abrir um perfil do tenant demo, ver as caixas do LogStream, salvar; abrir `/permission-catalog`. Relatar o que não pôde ser verificado.
- [ ] **Step 5: Commit** — `feat(adminportal): perfis por catalogo de permissoes, limpeza de orfas e pagina do catalogo (ADR-0039)`.

---

### Task 10: Prova por mutação

Sem commit de produção. Para cada linha: aplicar, rodar o filtro, ver FAIL, reverter, ver PASS. Não detectada → fortalecer o teste (commit `test(...)`).

| # | Invariante | Mutação |
| --- | --- | --- |
| 1 | escopo vs rota | `RequirePermissionsScopeAsync` aceita qualquer `permissions:*` |
| 2 | recusa só do acrescentado | `added` = `normalized` (todas as enviadas) |
| 3 | recusa existe | remover o bloco `if (unknown.Count > 0)` |
| 4 | publicar não toca perfil | `ReplaceAsync` também remove a permissão retirada dos `RoleClaims` |
| 5 | limpeza só no tenant da rota | remover o filtro de tenant na consulta da limpeza |
| 6 | limpeza não toca reservado | remover o `IsReservedName` da limpeza |
| 7 | `permissions:*` fora de client de produto | `IsProductScope` passa a aceitar prefixo `permissions:` |
| 8 | catálogo completo | tirar uma entrada de `LogStreamPermissions.Catalog` |
| 9 | colisão não bloqueia | handler devolve erro quando há colisão |

---

### Task 11: Documentação, verificação e publicação

- [ ] `docs/getting-started.md`: seção sobre declarar `Catalog` e ligar o publicador (código da Task 7), escopo `permissions:<produto>` no client de plataforma, e o efeito no `SetRolePermissions`.
- [ ] `CHANGELOG.md` em "Não publicado": SharedKernel, SDK EF Core, SecureGate.Client (métodos + publicador + `unknownPermissions`), Templates; nota de serviço (quebra: permissão nova só depois do catálogo publicado; declarar `permissions:<produto>` no `SecureGate:PlatformClients` do client de cada produto).
- [ ] `docs/roadmap.md` (#33 entregue; nota da colisão visível e do namespace adiado), `CLAUDE.md` (estado), `docs/design-decisions-log.md` (as perguntas, o ajuste do publicador e o resultado da mutação).
- [ ] Verificação: `dotnet build Secco.Platform.slnx --configuration Release` e `dotnet test Secco.Platform.slnx` (gravar TRX, ver memória da falha intermitente).
- [ ] Commit `docs: fecha a entrega do catalogo de permissoes (issue #33, ADR-0039)`.
- [ ] Publicação só com confirmação do usuário, pela skill `secco-platform-release`; fechar a #33.
