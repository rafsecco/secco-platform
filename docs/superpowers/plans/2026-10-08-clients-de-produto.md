# Clients OAuth de produto e de plataforma — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Client OAuth de produto vinculado a tenant, gerido por API no SecureGate, e clients de plataforma declarados em configuração e reconciliados no seed de referência (issue #31, ADR-0037).

**Architecture:** `OidcApplication` ganha `TenantId`/`Origin`/`Name`. O token de `client_credentials` de client vinculado sai com `tenant_id`, e a regra de conflito que já existe no `TenantResolver` do SDK contém o client no próprio tenant — SDK e produtos não mudam. A gestão segue o padrão da plataforma: porta na Application (`IProductClientStore`), adaptador OpenIddict na Infrastructure, minimal API com `Result` → `ToHttpResult()`. Clients sem tenant nascem só de `SecureGate:PlatformClients`, por um `IReferenceDataSeeder`.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, OpenIddict (EF Core stores), EF Core (SQL Server + PostgreSQL), xUnit + FluentAssertions 7 + NSubstitute + Testcontainers, NSwag.

**Spec:** `docs/superpowers/specs/2026-10-08-clients-de-produto-design.md` (e a ADR-0037 com a emenda, em `docs/adr/secco-platform-adrs.md`).

## Global Constraints

- Nunca contradizer ADR Aceita; ADR-0037 com emenda: reconciliação **no seed de referência**, nunca hosted service no startup.
- Erros de negócio via `Result`/`Error` do SharedKernel (ADR-0004); HTTP em ProblemDetails via `ToHttpResult()`.
- Domain/Application sem EF Core, sem OpenIddict, sem HTTP (ADR-0002). Options da Infrastructure ficam na Infrastructure.
- Banco: nomes por convention (ADR-0017) — nada de nome de coluna digitado à mão, exceto em SQL cru de check constraint e de migration de dados.
- `.WithName()` em todo endpoint de contrato; `operationId` exatamente: `CreateProductClient`, `ListProductClients`, `GetProductClient`, `UpdateProductClient`, `RotateProductClientSecret`, `DeleteProductClient`.
- Central Package Management: nenhum pacote novo nesta entrega.
- Build Release com warnings = erros: `dotnet build Secco.Platform.slnx --configuration Release`.
- Verificação final pelo comando documentado, na solution inteira: `dotnet test Secco.Platform.slnx`.
- Commits Conventional Commits em português, direto na `main`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Valores fixos da spec: `name` 1–100 sem caractere de controle; `scopes` 1–10 distintos; `roles` 0–20 distintos; `clientId` = `cli_` + 16 caracteres base32 minúsculos; secret = 32 bytes aleatórios em base64url; `ProductScopes = ["logstream", "notificationhub"]`; platform `ClientId` em `^[a-z0-9]+(-[a-z0-9]+)*$`, até 100, sem prefixo `cli_`; secret de plataforma ≥ 32 caracteres fora de Development.
- Secret nunca em log, nunca em `GET`; só na resposta de criação e de rotação.

## Review Focus

- **Nome de papel com caixa diferente** (`Admin` × `admin`): o Identity normaliza, então o client deve gravar o nome canônico de `tb_roles`, e a checagem de "perfil em uso" do `DeleteRole` deve ser case-insensitive e por token inteiro. Teste na Task 7.
- **Mesmo `name` em tenants diferentes** deve ser permitido (unicidade é por tenant), e o índice único não pode colidir entre clients de plataforma (`TenantId` nulo). Teste na Task 2.
- **`PUT` com o nome atual do próprio client** não pode responder `409` (a checagem de duplicata exclui o próprio client). Teste na Task 6.
- **Pedido de token sem `scope`** de um client de produto deve sair com `tenant_id` e sem escopos, não falhar. Teste na Task 3.
- **Reconciliação rodando duas vezes seguidas** sem mudar a configuração não pode re-hashear secret nem alterar nada. Teste na Task 10.

---

### Task 1: Lista fechada de escopos de produto

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateScopes.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/ProductScopesTests.cs`

**Interfaces:**
- Produces: `SecureGateScopes.ProductScopes` (`IReadOnlyList<string>`), `SecureGateScopes.IsProductScope(string scope)` → `bool`.

- [ ] **Step 1: Write the failing test**

```csharp
using FluentAssertions;
using Secco.SecureGate.Application;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

public class ProductScopesTests
{
	[Theory]
	[InlineData("logstream")]
	[InlineData("notificationhub")]
	public void IsProductScope_EscopoDeApiDeProduto_Aceita(string scope) =>
		SecureGateScopes.IsProductScope(scope).Should().BeTrue();

	[Theory]
	[InlineData("securegate:admin")]
	[InlineData("authorization:read")]
	[InlineData("catalog:logstream")]
	[InlineData("securegate")]
	[InlineData("openid")]
	[InlineData("LOGSTREAM")]
	[InlineData("")]
	public void IsProductScope_EscopoDeInfraestruturaOuDesconhecido_Recusa(string scope) =>
		SecureGateScopes.IsProductScope(scope).Should().BeFalse();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductScopesTests"`
Expected: FAIL de compilação — `IsProductScope` não existe.

- [ ] **Step 3: Write minimal implementation** — acrescentar ao fim da classe `SecureGateScopes`:

```csharp
	/// <summary>
	/// Escopos concedíveis a client de PRODUTO, vinculado a tenant (ADR-0037). Lista fechada no
	/// código: produto novo entra aqui no mesmo PR que registra o escopo no seed de referência.
	/// <see cref="Admin"/>, <see cref="AuthorizationRead"/>, <c>catalog:*</c> e <c>securegate</c>
	/// são de infraestrutura — atravessam tenant por construção e só existem em client de plataforma.
	/// </summary>
	public static readonly IReadOnlyList<string> ProductScopes = ["logstream", "notificationhub"];

	/// <summary>Indica se o escopo pode ser concedido a client de produto (comparação exata, ordinal).</summary>
	/// <param name="scope">Escopo candidato.</param>
	public static bool IsProductScope(string scope) => ProductScopes.Contains(scope, StringComparer.Ordinal);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductScopesTests"`
Expected: PASS (9 casos).

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Application/SecureGateScopes.cs tests/SecureGate/Secco.SecureGate.Tests/Unit/ProductScopesTests.cs
git commit -m "feat(securegate): lista fechada de escopos de client de produto (ADR-0037)"
```

---

### Task 2: Modelo — tenant, origem e nome no client OIDC

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Clients/ClientOrigin.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/OpenIddict/OidcEntities.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Contexts/SecureGateDbContext.cs:116-120`
- Create: migration `AddProductClients` em `src/SecureGate/Secco.SecureGate.Migrations.SqlServer/Migrations/` e `src/SecureGate/Secco.SecureGate.Migrations.Postgres/Migrations/`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/PlatformSchemaTests.cs` (novos testes)

**Interfaces:**
- Produces: `enum ClientOrigin { Configuration = 0, Api = 1 }` (namespace `Secco.SecureGate.Application.Clients`); `OidcApplication.TenantId` (`Guid?`), `OidcApplication.Origin` (`ClientOrigin`), `OidcApplication.Name` (`string?`, máx. `OidcApplication.NameMaxLength = 100`). Índice único `uk_oidc_applications_id_fk_tenant_ds_name`; check constraint `ck_oidc_applications_origin_tenant`.

- [ ] **Step 1: Write the failing tests** — acrescentar a `PlatformSchemaTests`:

```csharp
	[Fact]
	public async Task OidcApplication_ClientDeApiSemTenant_ViolaCheckConstraint()
	{
		await using var context = CreateContext();
		context.Set<Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication>().Add(new()
		{
			ClientId = $"cli_{Guid.NewGuid():N}"[..20],
			Origin = Secco.SecureGate.Application.Clients.ClientOrigin.Api,
			TenantId = null,
			Name = "sem tenant",
		});

		var act = () => context.SaveChangesAsync();

		await act.Should().ThrowAsync<DbUpdateException>("Api ⇔ tenant preenchido (ADR-0037)");
	}

	[Fact]
	public async Task OidcApplication_MesmoNomeEmTenantsDiferentes_EhPermitido()
	{
		await using var context = CreateContext();
		var tenantA = new Tenant("A", $"a-{Guid.NewGuid():N}");
		var tenantB = new Tenant("B", $"b-{Guid.NewGuid():N}");
		context.Tenants.AddRange(tenantA, tenantB);
		await context.SaveChangesAsync();

		foreach (var tenant in new[] { tenantA, tenantB })
		{
			context.Set<Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication>().Add(new()
			{
				ClientId = $"cli_{Guid.NewGuid():N}"[..20],
				Origin = Secco.SecureGate.Application.Clients.ClientOrigin.Api,
				TenantId = tenant.Id,
				Name = "Sistema de compras",
			});
		}

		var act = () => context.SaveChangesAsync();

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task OidcApplication_MesmoNomeNoMesmoTenant_ViolaIndiceUnico()
	{
		await using var context = CreateContext();
		var tenant = new Tenant("C", $"c-{Guid.NewGuid():N}");
		context.Tenants.Add(tenant);
		await context.SaveChangesAsync();

		for (var i = 0; i < 2; i++)
		{
			context.Set<Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication>().Add(new()
			{
				ClientId = $"cli_{Guid.NewGuid():N}"[..20],
				Origin = Secco.SecureGate.Application.Clients.ClientOrigin.Api,
				TenantId = tenant.Id,
				Name = "Duplicado",
			});
		}

		var act = () => context.SaveChangesAsync();

		await act.Should().ThrowAsync<DbUpdateException>();
	}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~PlatformSchemaTests"`
Expected: FAIL de compilação — `ClientOrigin`/`Origin`/`TenantId`/`Name` não existem.

- [ ] **Step 3: Create the enum** — `src/SecureGate/Secco.SecureGate.Application/Clients/ClientOrigin.cs`:

```csharp
namespace Secco.SecureGate.Application.Clients;

/// <summary>
/// Por onde um client OIDC nasceu (ADR-0037). Cada caminho só enxerga e altera os seus: a
/// reconciliação da configuração nunca toca client da API, e a API nunca toca client de plataforma.
/// </summary>
public enum ClientOrigin
{
	/// <summary>Client de PLATAFORMA, sem tenant, declarado em <c>SecureGate:PlatformClients</c>.</summary>
	Configuration = 0,

	/// <summary>Client de PRODUTO, vinculado a um tenant, registrado pela API.</summary>
	Api = 1,
}
```

- [ ] **Step 4: Extend the entity** — em `OidcEntities.cs`, dentro de `OidcApplication`, depois de `Roles`:

```csharp
	/// <summary>Tamanho máximo do nome legível do client (ADR-0037).</summary>
	public const int NameMaxLength = 100;

	/// <summary>
	/// Tenant do client de PRODUTO (coluna <c>id_fk_tenant</c>, ADR-0037). Preenchido: o token de
	/// client credentials sai com <c>tenant_id</c>. Nulo: client de plataforma.
	/// </summary>
	public Guid? TenantId { get; set; }

	/// <summary>Origem do client (coluna <c>ie_origin</c>, ADR-0037).</summary>
	public Application.Clients.ClientOrigin Origin { get; set; } = Application.Clients.ClientOrigin.Configuration;

	/// <summary>
	/// Nome legível (coluna <c>ds_name</c>). Chave natural do client de produto dentro do tenant
	/// (ADR-0034); no client de plataforma é o próprio <c>ClientId</c>.
	/// </summary>
	public string? Name { get; set; }
```

- [ ] **Step 5: Map it** — em `SecureGateDbContext.OnModelCreating`, substituir o bloco `builder.Entity<OidcApplication>(...)` por:

```csharp
		builder.Entity<OidcApplication>(application =>
		{
			application.ToTable("tb_oidc_applications", table =>
				// Api ⇔ tenant preenchido (ADR-0037). SQL portável: identificadores minúsculos sem
				// aspas valem igual em SQL Server e PostgreSQL.
				table.HasCheckConstraint(
					"ck_oidc_applications_origin_tenant",
					"(ie_origin = 1 AND id_fk_tenant IS NOT NULL) OR (ie_origin = 0 AND id_fk_tenant IS NULL)"));
			application.Property(a => a.Roles).HasMaxLength(OidcApplication.RolesMaxLength);
			application.Property(a => a.Name).HasMaxLength(OidcApplication.NameMaxLength);
			application.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);

			// Nome único por tenant. Client de plataforma usa Name = ClientId, então dois clients
			// de plataforma (TenantId nulo, que o SQL Server trata como iguais no índice) nunca colidem.
			application.HasIndex(a => new { a.TenantId, a.Name }).IsUnique()
				.HasDatabaseName("uk_oidc_applications_id_fk_tenant_ds_name");
		});
```

- [ ] **Step 6: Generate the migrations**

Run:
```bash
dotnet ef migrations add AddProductClients --project src/SecureGate/Secco.SecureGate.Migrations.SqlServer/Secco.SecureGate.Migrations.SqlServer.csproj --output-dir Migrations
dotnet ef migrations add AddProductClients --project src/SecureGate/Secco.SecureGate.Migrations.Postgres/Secco.SecureGate.Migrations.Postgres.csproj --output-dir Migrations
```
Expected: duas migrations criadas, com colunas `id_fk_tenant`, `ie_origin`, `ds_name`, FK, índice único e check constraint.

- [ ] **Step 7: Backfill `ds_name` nas linhas existentes** — em **cada** migration gerada, no `Up`, logo depois do `AddColumn` de `ds_name` e **antes** do `CreateIndex` do índice único, inserir:

```csharp
            // Clients existentes viram de plataforma (ie_origin = 0 pelo default) com Name = ClientId,
            // o que mantém o índice único (tenant, nome) sem colisão (ADR-0037).
            migrationBuilder.Sql("UPDATE tb_oidc_applications SET ds_name = ds_client_id WHERE ds_name IS NULL");
```

Se o gerador colocou o `CreateIndex` antes do `AddColumn` de `ds_name`, reordenar para: colunas → `UPDATE` → índice → check constraint → FK.

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~PlatformSchemaTests"`
Expected: PASS, incluindo `Migrations_Always_ApplyFromScratch`.

- [ ] **Step 9: Commit**

```bash
git add src/SecureGate tests/SecureGate/Secco.SecureGate.Tests/Integration/PlatformSchemaTests.cs
git commit -m "feat(securegate): tenant, origem e nome no client OIDC (ADR-0037)"
```

---

### Task 3: Token de client de produto com `tenant_id`

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Api/Endpoints/TokenEndpoints.cs` (`MapTokenEndpoints` e `HandleClientCredentialsAsync`)
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/SecureGateApiFactory.cs` (helper novo)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/ProductClientTokenTests.cs`

**Interfaces:**
- Consumes: `OidcApplication.TenantId/Origin/Name` (Task 2), `SecureGateScopes.IsProductScope` (Task 1), `ITenantRepository.GetByIdAsync` (existente).
- Produces: helper de teste `SecureGateApiFactory.CreateProductClientAsync(Guid tenantId, string clientId, string clientSecret, string? roles, params string[] scopes)`.

- [ ] **Step 1: Add the test helper** — em `SecureGateApiFactory`, depois de `CreateClientWithRolesAsync`:

```csharp
	/// <summary>
	/// Registra direto no banco um client de PRODUTO vinculado ao tenant (ADR-0037) — atalho de
	/// teste para exercitar a emissão sem depender da API de gestão. Permissões gravadas como
	/// vierem, inclusive escopo proibido: é assim que os testes provam a checagem na emissão.
	/// </summary>
	public async Task CreateProductClientAsync(
		Guid tenantId, string clientId, string clientSecret, string? roles, params string[] scopes)
	{
		using var scope = Services.CreateScope();
		var applications = scope.ServiceProvider
			.GetRequiredService<OpenIddict.Core.OpenIddictApplicationManager<Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication>>();

		var application = new Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication
		{
			TenantId = tenantId,
			Origin = Secco.SecureGate.Application.Clients.ClientOrigin.Api,
			Name = clientId,
			Roles = roles,
		};

		var descriptor = new OpenIddict.Abstractions.OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientType = OpenIddict.Abstractions.OpenIddictConstants.ClientTypes.Confidential,
			DisplayName = clientId,
		};
		descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Endpoints.Token);
		descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);

		foreach (var scopeName in scopes)
		{
			descriptor.Permissions.Add(OpenIddict.Abstractions.OpenIddictConstants.Permissions.Prefixes.Scope + scopeName);
		}

		await applications.PopulateAsync(application, descriptor);
		await applications.CreateAsync(application, clientSecret);
	}
```

- [ ] **Step 2: Write the failing tests** — `ProductClientTokenTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Secco.SecureGate.Infrastructure.Contexts;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Emissão de token para client de produto vinculado a tenant (ADR-0037).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ProductClientTokenTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private const string Secret = "product-client-secret-de-32-chars-min!!";
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private static string NewClientId() => $"cli_{Guid.NewGuid():N}"[..20];

	private Task<HttpResponseMessage> RequestTokenAsync(string clientId, string? scope)
	{
		var form = new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = Secret,
		};

		if (scope is not null)
		{
			form["scope"] = scope;
		}

		return factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(form));
	}

	private static async Task<JsonWebToken> ReadTokenAsync(HttpResponseMessage response)
	{
		var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
		return new JsonWebTokenHandler().ReadJsonWebToken(payload.GetProperty("access_token").GetString());
	}

	[Fact]
	public async Task ClientCredentials_ClientDeProduto_TokenSaiComTenantIdERole()
	{
		var clientId = NewClientId();
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, "compras-writer", "logstream");

		var response = await RequestTokenAsync(clientId, "logstream");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		var token = await ReadTokenAsync(response);
		token.GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString());
		token.GetClaim("role").Value.Should().Be("compras-writer");
		token.GetClaim("sub").Value.Should().Be(clientId);
	}

	[Fact]
	public async Task ClientCredentials_ClientDeProdutoSemScope_TokenSaiComTenantId()
	{
		var clientId = NewClientId();
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, roles: null, "logstream");

		var response = await RequestTokenAsync(clientId, scope: null);

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await ReadTokenAsync(response)).GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString());
	}

	[Fact]
	public async Task ClientCredentials_ClientDePlataforma_TokenSaiSemTenantId()
	{
		var clientId = $"plataforma-{Guid.NewGuid():N}"[..30];
		await factory.CreateClientAsync(clientId, Secret, "logstream");

		var response = await RequestTokenAsync(clientId, "logstream");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		(await ReadTokenAsync(response)).Claims.Should().NotContain(c => c.Type == "tenant_id");
	}

	[Fact]
	public async Task ClientCredentials_TenantDesativado_RecusaComInvalidClient()
	{
		var clientId = NewClientId();
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, roles: null, "logstream");

		using (var scope = factory.Services.CreateScope())
		{
			var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
			var tenant = await context.Tenants.FindAsync(_tenantId);
			tenant!.Deactivate();
			await context.SaveChangesAsync();
		}

		var response = await RequestTokenAsync(clientId, "logstream");

		response.IsSuccessStatusCode.Should().BeFalse();
		(await response.Content.ReadAsStringAsync()).Should().Contain("invalid_client");
	}

	[Fact]
	public async Task ClientCredentials_EscopoDeInfraestruturaGravadoNoBanco_RecusaNaEmissao()
	{
		var clientId = NewClientId();
		// Simula edição direta do banco: a API nunca grava isto (Task 4), a emissão precisa barrar mesmo assim
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, roles: null,
			"logstream", Secco.SecureGate.Application.SecureGateScopes.Admin);

		var response = await RequestTokenAsync(clientId, Secco.SecureGate.Application.SecureGateScopes.Admin);

		response.IsSuccessStatusCode.Should().BeFalse();
		(await response.Content.ReadAsStringAsync()).Should().Contain("invalid_scope");
	}
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientTokenTests"`
Expected: FAIL — `tenant_id` ausente; tenant desativado e escopo de infraestrutura recebem token.

- [ ] **Step 4: Implement** — em `TokenEndpoints.MapTokenEndpoints`, acrescentar `ITenantRepository tenants` aos parâmetros do lambda e passá-lo: `return await HandleClientCredentialsAsync(context, request, scopeManager, applicationManager, tenants);`. Substituir `HandleClientCredentialsAsync` por:

```csharp
	private static async Task<IResult> HandleClientCredentialsAsync(
		HttpContext context,
		OpenIddictRequest request,
		IOpenIddictScopeManager scopeManager,
		IOpenIddictApplicationManager applicationManager,
		ITenantRepository tenants)
	{
		// Credenciais e permissões de scope já validadas pelo OpenIddict (client_secret hasheado)
		var application = await applicationManager.FindByClientIdAsync(request.ClientId!, context.RequestAborted)
			as Secco.SecureGate.Infrastructure.OpenIddict.OidcApplication;

		var identity = new ClaimsIdentity(
			TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);

		// Claims curtas (ADR-0007): sub = client
		identity.SetClaim(Claims.Subject, request.ClientId);
		identity.SetScopes(request.GetScopes());

		if (application?.TenantId is { } tenantId)
		{
			// Client de PRODUTO (ADR-0037): o token carrega o tenant do vínculo, e a regra de conflito
			// do TenantResolver passa a conter o client nele. Desativar o tenant precisa parar as
			// máquinas dele também — sem esta checagem, renovariam token a cada 5 minutos para sempre.
			if (await tenants.GetByIdAsync(tenantId, context.RequestAborted) is not { IsActive: true })
			{
				return Forbid(Errors.InvalidClient, "O client não pode obter token.");
			}

			// Defesa em profundidade: o registro já barra, mas o banco pode ser editado à mão
			if (request.GetScopes().Any(scope => !SecureGateScopes.IsProductScope(scope)))
			{
				return Forbid(Errors.InvalidScope, "Escopo não permitido a este client.");
			}

			identity.SetClaim(SeccoClaims.TenantId, tenantId.ToString());
		}

		// Sem tenant: client de PLATAFORMA (ADR-0037), só nasce da configuração da instalação — o
		// tenant alvo viaja no header X-Tenant-Id (caminho "sem claim → header", ADR-0005/0024).

		// Roles do client (Fase 6.4, ADR-0021): máquinas usam o MESMO modelo
		// Role + Permission dos usuários — a claim curta 'role' sai no access token
		if (application is { Roles.Length: > 0 })
		{
			identity.SetClaims(Claims.Role,
				[.. application.Roles.Split(' ', StringSplitOptions.RemoveEmptyEntries)]);
		}

		identity.SetResources(await OidcPrincipalBuilder.ResolveResourcesAsync(
			scopeManager, identity.GetScopes(), context.RequestAborted));
		identity.SetDestinations(static _ => [Destinations.AccessToken]);
```

Manter o restante do método original (o `return Results.SignIn(...)` que já vinha depois de `SetDestinations`). Acrescentar `using Secco.SharedKernel.Constants;` se ainda não houver.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientTokenTests|FullyQualifiedName~ClientCredentialsFlowTests|FullyQualifiedName~CrossProductTokenFlowTests"`
Expected: PASS — os novos e os antigos (client de plataforma inalterado).

- [ ] **Step 6: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Api/Endpoints/TokenEndpoints.cs tests/SecureGate/Secco.SecureGate.Tests/Integration
git commit -m "feat(securegate): token de client de produto carrega tenant_id e recusa tenant inativo (ADR-0037)"
```

---

### Task 4: Application — regras, porta e casos de uso dos clients de produto

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Clients/ProductClientRules.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Clients/ProductClientDtos.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Clients/IProductClientStore.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Clients/ProductClientAccessValidator.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Clients/ProductClientHandlers.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateErrors.cs` (classe `Clients` nova)
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateApplicationExtensions.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/ProductClientRulesTests.cs`, `tests/SecureGate/Secco.SecureGate.Tests/Unit/ProductClientHandlersTests.cs`

**Interfaces:**
- Consumes: `IRoleRepository.TenantExistsAsync`, `IRoleRepository.FindRoleAsync` (existentes), `ITenantRepository.GetByIdAsync`, `RoleInputRules.IsValidName/IsAssignableToUsers`, `SecureGateScopes.IsProductScope`.
- Produces:
  - `ProductClientRules`: `NameMaxLength = 100`, `MaxScopes = 10`, `MaxRoles = 20`, `ClientIdPrefix = "cli_"`, `string NewClientId()`, `string NewSecret()`, `bool IsValidName(string)`.
  - DTOs: `ProductClientDto(string ClientId, string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles, DateTimeOffset? CreatedAt)`, `CreatedProductClientDto(string ClientId, string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles, DateTimeOffset? CreatedAt, string ClientSecret)`, `ProductClientSecretDto(string ClientId, string ClientSecret)`.
  - `ProductClientAccess(string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles)`.
  - `IProductClientStore` (abaixo).
  - Handlers: `CreateProductClientHandler`, `ListProductClientsHandler`, `GetProductClientHandler`, `UpdateProductClientHandler`, `RotateProductClientSecretHandler`, `DeleteProductClientHandler`.
  - Comando `ProductClientCommand(string? Name, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? Roles)`.

- [ ] **Step 1: Write the failing rules tests** — `ProductClientRulesTests.cs`:

```csharp
using FluentAssertions;
using Secco.SecureGate.Application.Clients;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

public class ProductClientRulesTests
{
	[Fact]
	public void NewClientId_SempreTemPrefixoE16CaracteresBase32Minusculos()
	{
		var clientId = ProductClientRules.NewClientId();

		clientId.Should().MatchRegex("^cli_[a-z2-7]{16}$");
	}

	[Fact]
	public void NewClientId_DuasChamadas_GeramValoresDiferentes() =>
		ProductClientRules.NewClientId().Should().NotBe(ProductClientRules.NewClientId());

	[Fact]
	public void NewSecret_Sempre32BytesEmBase64Url()
	{
		var secret = ProductClientRules.NewSecret();

		secret.Should().MatchRegex("^[A-Za-z0-9_-]{43}$", "32 bytes em base64url sem padding são 43 caracteres");
	}

	[Theory]
	[InlineData("Sistema de compras", true)]
	[InlineData("a", true)]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData("nome\u0007com controle", false)]
	public void IsValidName_CasosDeFormato(string name, bool expected) =>
		ProductClientRules.IsValidName(name.Trim()).Should().Be(expected);

	[Fact]
	public void IsValidName_AcimaDe100_Recusa() =>
		ProductClientRules.IsValidName(new string('x', 101)).Should().BeFalse();
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientRulesTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement the rules** — `ProductClientRules.cs`:

```csharp
using System.Security.Cryptography;

namespace Secco.SecureGate.Application.Clients;

/// <summary>Regras de formato e geração de credencial do client de produto (ADR-0037, ADR-0020).</summary>
public static class ProductClientRules
{
	/// <summary>Tamanho máximo do nome.</summary>
	public const int NameMaxLength = 100;

	/// <summary>Máximo de escopos por client.</summary>
	public const int MaxScopes = 10;

	/// <summary>Máximo de papéis por client.</summary>
	public const int MaxRoles = 20;

	/// <summary>
	/// Prefixo de todo <c>client_id</c> gerado pela API. A configuração de plataforma é proibida de
	/// usá-lo, então os dois espaços de nome nunca se cruzam.
	/// </summary>
	public const string ClientIdPrefix = "cli_";

	private const string Base32Alphabet = "abcdefghijklmnopqrstuvwxyz234567";

	/// <summary>Gera um <c>client_id</c> aleatório — o chamador nunca escolhe (sem colisão nem imitação).</summary>
	public static string NewClientId()
	{
		Span<char> suffix = stackalloc char[16];

		for (var i = 0; i < suffix.Length; i++)
		{
			suffix[i] = Base32Alphabet[RandomNumberGenerator.GetInt32(Base32Alphabet.Length)];
		}

		return ClientIdPrefix + new string(suffix);
	}

	/// <summary>Gera um secret de 32 bytes aleatórios em base64url, exibido uma única vez.</summary>
	public static string NewSecret() =>
		Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
			.TrimEnd('=').Replace('+', '-').Replace('/', '_');

	/// <summary>Valida um nome já aparado: 1–100 caracteres, sem caractere de controle.</summary>
	/// <param name="name">Nome candidato.</param>
	public static bool IsValidName(string name) =>
		name.Length is > 0 and <= NameMaxLength && !name.Any(char.IsControl);
}
```

- [ ] **Step 4: Run rules tests to pass**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientRulesTests"`
Expected: PASS.

- [ ] **Step 5: Add the errors** — em `SecureGateErrors`, nova classe aninhada antes de `Credentials`:

```csharp
	/// <summary>Erros de client de produto (ADR-0037).</summary>
	public static class Clients
	{
		/// <summary>Client não encontrado neste tenant — também para client de outro tenant ou de plataforma.</summary>
		public static readonly Error NotFound =
			Error.NotFound("SecureGate.Client.NotFound", "Client não encontrado neste tenant.");

		/// <summary>Nome ausente, longo demais ou com caractere de controle.</summary>
		public static readonly Error NameInvalid =
			Error.Validation("SecureGate.Client.NameInvalid",
				$"O nome do client é obrigatório, com até {Application.Clients.ProductClientRules.NameMaxLength} caracteres e sem caractere de controle.");

		/// <summary>Já existe client com este nome no tenant (chave natural, ADR-0034).</summary>
		public static readonly Error NameAlreadyExists =
			Error.Conflict("SecureGate.Client.NameAlreadyExists", "Já existe um client com este nome neste tenant.");

		/// <summary>Lista de escopos vazia, longa demais ou com repetição.</summary>
		public static readonly Error ScopesInvalid =
			Error.Validation("SecureGate.Client.ScopesInvalid",
				$"Informe de 1 a {Application.Clients.ProductClientRules.MaxScopes} escopos distintos.");

		/// <summary>Escopo fora da lista de produto — inclui todo escopo de infraestrutura.</summary>
		public static readonly Error ScopeNotAllowed =
			Error.Validation("SecureGate.Client.ScopeNotAllowed",
				$"Escopo não concedível a client de produto. Permitidos: {string.Join(", ", SecureGateScopes.ProductScopes)}.");

		/// <summary>Lista de papéis longa demais ou com repetição.</summary>
		public static readonly Error RolesInvalid =
			Error.Validation("SecureGate.Client.RolesInvalid",
				$"Informe até {Application.Clients.ProductClientRules.MaxRoles} papéis distintos.");

		/// <summary>Papel inexistente no tenant.</summary>
		public static readonly Error RoleNotFound =
			Error.Validation("SecureGate.Client.RoleNotFound", "Um dos papéis informados não existe neste tenant.");

		/// <summary>Papel reservado à estrutura de instalação.</summary>
		public static readonly Error RoleNotAssignable =
			Error.Validation("SecureGate.Client.RoleNotAssignable", "Um dos papéis informados é reservado e não pode ser atribuído a client.");

		/// <summary>Tenant desativado não recebe client novo.</summary>
		public static readonly Error TenantInactive =
			Error.Conflict("SecureGate.Client.TenantInactive", "O tenant está desativado.");
	}
```

- [ ] **Step 6: Add DTOs, store port and validator**

`ProductClientDtos.cs`:

```csharp
namespace Secco.SecureGate.Application.Clients;

/// <summary>Client de produto como a API o expõe — nunca com o secret (ADR-0037).</summary>
public sealed record ProductClientDto(
	string ClientId, string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles, DateTimeOffset? CreatedAt);

/// <summary>Resposta da criação: o único momento, junto da rotação, em que o secret aparece.</summary>
public sealed record CreatedProductClientDto(
	string ClientId, string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles, DateTimeOffset? CreatedAt,
	string ClientSecret);

/// <summary>Resposta da rotação de secret.</summary>
public sealed record ProductClientSecretDto(string ClientId, string ClientSecret);

/// <summary>Entrada de criação e de alteração de acesso (conjunto completo).</summary>
public sealed record ProductClientCommand(string? Name, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? Roles);

/// <summary>Acesso já validado: nome aparado, escopos de produto, papéis com o nome canônico do tenant.</summary>
public sealed record ProductClientAccess(string Name, IReadOnlyList<string> Scopes, IReadOnlyList<string> Roles);
```

`IProductClientStore.cs`:

```csharp
namespace Secco.SecureGate.Application.Clients;

/// <summary>
/// Persistência dos clients de PRODUTO (ADR-0037). Toda operação filtra por tenant E por origem
/// <see cref="ClientOrigin.Api"/>: client de outro tenant ou de plataforma é invisível aqui.
/// </summary>
public interface IProductClientStore
{
	/// <summary>Indica se o nome já está em uso no tenant, ignorando o próprio client na alteração.</summary>
	Task<bool> NameExistsAsync(Guid tenantId, string name, string? exceptClientId, CancellationToken cancellationToken = default);

	/// <summary>Cria o client com o id e o secret informados (o secret persiste só como hash).</summary>
	Task<ProductClientDto> CreateAsync(
		Guid tenantId, string clientId, string clientSecret, ProductClientAccess access, CancellationToken cancellationToken = default);

	/// <summary>Lista os clients do tenant, por nome.</summary>
	Task<IReadOnlyList<ProductClientDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Busca um client do tenant; nulo se não existir aqui.</summary>
	Task<ProductClientDto?> GetAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default);

	/// <summary>Substitui nome, escopos e papéis. Falso se o client não existir no tenant.</summary>
	Task<bool> UpdateAsync(Guid tenantId, string clientId, ProductClientAccess access, CancellationToken cancellationToken = default);

	/// <summary>Troca o secret na hora. Falso se o client não existir no tenant.</summary>
	Task<bool> RotateSecretAsync(Guid tenantId, string clientId, string clientSecret, CancellationToken cancellationToken = default);

	/// <summary>Remove o client (e os tokens/autorizações do OpenIddict). Falso se não existir no tenant.</summary>
	Task<bool> DeleteAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default);
}
```

`ProductClientAccessValidator.cs`:

```csharp
using Secco.SecureGate.Application.Roles;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Clients;

/// <summary>
/// Validação comum a criação e alteração (ADR-0037, ADR-0020): formato antes do banco, e papéis
/// resolvidos para o nome canônico de <c>tb_roles</c> — o token carrega exatamente o que o
/// resolvedor de permissões vai procurar.
/// </summary>
public sealed class ProductClientAccessValidator(IRoleRepository roles)
{
	/// <summary>Valida o comando e devolve o acesso normalizado.</summary>
	public async Task<Result<ProductClientAccess>> ValidateAsync(
		Guid tenantId, ProductClientCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var name = command.Name?.Trim() ?? string.Empty;

		if (!ProductClientRules.IsValidName(name))
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.NameInvalid);
		}

		var scopes = command.Scopes ?? [];

		if (scopes.Count is 0 or > ProductClientRules.MaxScopes || scopes.Distinct(StringComparer.Ordinal).Count() != scopes.Count)
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.ScopesInvalid);
		}

		if (scopes.Any(scope => !SecureGateScopes.IsProductScope(scope)))
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.ScopeNotAllowed);
		}

		var requestedRoles = (command.Roles ?? []).Select(role => role?.Trim() ?? string.Empty).ToList();

		if (requestedRoles.Count > ProductClientRules.MaxRoles
			|| requestedRoles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requestedRoles.Count)
		{
			return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RolesInvalid);
		}

		var canonicalRoles = new List<string>(requestedRoles.Count);

		foreach (var role in requestedRoles)
		{
			if (!RoleInputRules.IsValidName(role))
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RoleNotFound);
			}

			// Antes do banco: a lista de reservados é pública. Inclui o de operador — "assinável a
			// usuários" não vale para máquina: client de produto nunca é operador de instalação.
			if (RoleInputRules.IsReservedName(role))
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RoleNotAssignable);
			}

			if (await roles.FindRoleAsync(tenantId, role, cancellationToken).ConfigureAwait(false) is not { } found)
			{
				return Result.Failure<ProductClientAccess>(SecureGateErrors.Clients.RoleNotFound);
			}

			canonicalRoles.Add(found.Name);
		}

		return Result.Success(new ProductClientAccess(name, [.. scopes], canonicalRoles));
	}
}
```

- [ ] **Step 7: Write the failing handler tests** — `ProductClientHandlersTests.cs`:

```csharp
using FluentAssertions;
using NSubstitute;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Application.Roles;
using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Domain.Tenants;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

public class ProductClientHandlersTests
{
	private static readonly Guid TenantId = Guid.NewGuid();

	private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
	private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
	private readonly IProductClientStore _store = Substitute.For<IProductClientStore>();

	public ProductClientHandlersTests()
	{
		_tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(new Tenant("T", "t"));
		_roles.FindRoleAsync(TenantId, "compras-writer", Arg.Any<CancellationToken>())
			.Returns(new RoleSummaryData(Guid.NewGuid(), "compras-writer", 0));
		_store.CreateAsync(TenantId, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ProductClientAccess>(), Arg.Any<CancellationToken>())
			.Returns(call => new ProductClientDto(
				call.ArgAt<string>(1), call.ArgAt<ProductClientAccess>(3).Name,
				call.ArgAt<ProductClientAccess>(3).Scopes, call.ArgAt<ProductClientAccess>(3).Roles, DateTimeOffset.UtcNow));
	}

	private CreateProductClientHandler Create() =>
		new(_tenants, new ProductClientAccessValidator(_roles), _store);

	[Fact]
	public async Task Create_Valido_DevolveSecretUmaVezEClientIdGerado()
	{
		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("Sistema de compras", ["logstream"], ["compras-writer"]));

		result.IsSuccess.Should().BeTrue();
		result.Value.ClientId.Should().StartWith("cli_");
		result.Value.ClientSecret.Should().HaveLength(43);
		result.Value.Roles.Should().Equal("compras-writer");
	}

	[Theory]
	[InlineData("securegate:admin")]
	[InlineData("authorization:read")]
	[InlineData("catalog:logstream")]
	[InlineData("securegate")]
	public async Task Create_EscopoDeInfraestrutura_Recusa(string scope)
	{
		var result = await Create().HandleAsync(TenantId, new ProductClientCommand("x", [scope], []));

		result.Error.Should().Be(SecureGateErrors.Clients.ScopeNotAllowed);
		await _store.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default!, default!, default);
	}

	[Fact]
	public async Task Create_PapelReservado_Recusa()
	{
		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("x", ["logstream"], [SecureGatePlatform.OperatorRole]));

		result.Error.Should().Be(SecureGateErrors.Clients.RoleNotAssignable);
	}

	[Fact]
	public async Task Create_PapelInexistente_Recusa()
	{
		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("x", ["logstream"], ["nao-existe"]));

		result.Error.Should().Be(SecureGateErrors.Clients.RoleNotFound);
	}

	[Fact]
	public async Task Create_NomeDuplicado_Conflito()
	{
		_store.NameExistsAsync(TenantId, "Sistema de compras", null, Arg.Any<CancellationToken>()).Returns(true);

		var result = await Create().HandleAsync(TenantId,
			new ProductClientCommand("Sistema de compras", ["logstream"], []));

		result.Error.Should().Be(SecureGateErrors.Clients.NameAlreadyExists);
	}

	[Fact]
	public async Task Create_TenantInexistente_NotFound()
	{
		var result = await Create().HandleAsync(Guid.NewGuid(), new ProductClientCommand("x", ["logstream"], []));

		result.Error.Should().Be(SecureGateErrors.Tenants.NotFound);
	}

	[Fact]
	public async Task Create_TenantDesativado_Conflito()
	{
		var inactive = new Tenant("T", "t");
		inactive.Deactivate();
		_tenants.GetByIdAsync(TenantId, Arg.Any<CancellationToken>()).Returns(inactive);

		var result = await Create().HandleAsync(TenantId, new ProductClientCommand("x", ["logstream"], []));

		result.Error.Should().Be(SecureGateErrors.Clients.TenantInactive);
	}

	[Fact]
	public async Task Update_MesmoNomeDoProprioClient_NaoConflita()
	{
		_store.NameExistsAsync(TenantId, "Sistema de compras", "cli_aaaaaaaaaaaaaaaa", Arg.Any<CancellationToken>())
			.Returns(false);
		_store.UpdateAsync(TenantId, "cli_aaaaaaaaaaaaaaaa", Arg.Any<ProductClientAccess>(), Arg.Any<CancellationToken>())
			.Returns(true);

		var result = await new UpdateProductClientHandler(new ProductClientAccessValidator(_roles), _store)
			.HandleAsync(TenantId, "cli_aaaaaaaaaaaaaaaa", new ProductClientCommand("Sistema de compras", ["logstream"], []));

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task Rotate_ClientInexistente_NotFound()
	{
		var result = await new RotateProductClientSecretHandler(_store).HandleAsync(TenantId, "cli_bbbbbbbbbbbbbbbb");

		result.Error.Should().Be(SecureGateErrors.Clients.NotFound);
	}
}
```

- [ ] **Step 8: Run to verify fail**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientHandlersTests"`
Expected: FAIL de compilação — handlers não existem.

- [ ] **Step 9: Implement the handlers** — `ProductClientHandlers.cs`:

```csharp
using Secco.SecureGate.Application.Tenants;
using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Clients;

/// <summary>Registra client de produto vinculado ao tenant (ADR-0037). Secret exibido uma única vez.</summary>
public sealed class CreateProductClientHandler(
	ITenantRepository tenants, ProductClientAccessValidator validator, IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<CreatedProductClientDto>> HandleAsync(
		Guid tenantId, ProductClientCommand command, CancellationToken cancellationToken = default)
	{
		if (await tenants.GetByIdAsync(tenantId, cancellationToken).ConfigureAwait(false) is not { } tenant)
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Tenants.NotFound);
		}

		if (!tenant.IsActive)
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Clients.TenantInactive);
		}

		var validation = await validator.ValidateAsync(tenantId, command, cancellationToken).ConfigureAwait(false);

		if (validation.IsFailure)
		{
			return Result.Failure<CreatedProductClientDto>(validation.Error);
		}

		var access = validation.Value;

		if (await store.NameExistsAsync(tenantId, access.Name, exceptClientId: null, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure<CreatedProductClientDto>(SecureGateErrors.Clients.NameAlreadyExists);
		}

		var secret = ProductClientRules.NewSecret();
		var created = await store.CreateAsync(tenantId, ProductClientRules.NewClientId(), secret, access, cancellationToken)
			.ConfigureAwait(false);

		return Result.Success(new CreatedProductClientDto(
			created.ClientId, created.Name, created.Scopes, created.Roles, created.CreatedAt, secret));
	}
}

/// <summary>Lista os clients de produto do tenant.</summary>
public sealed class ListProductClientsHandler(IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<IReadOnlyList<ProductClientDto>>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		Result.Success(await store.ListAsync(tenantId, cancellationToken).ConfigureAwait(false));
}

/// <summary>Detalha um client de produto do tenant.</summary>
public sealed class GetProductClientHandler(IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ProductClientDto>> HandleAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		await store.GetAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is { } client
			? Result.Success(client)
			: Result.Failure<ProductClientDto>(SecureGateErrors.Clients.NotFound);
}

/// <summary>Substitui nome, escopos e papéis — PUT idempotente de fato (ADR-0034). Não mexe no secret.</summary>
public sealed class UpdateProductClientHandler(ProductClientAccessValidator validator, IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result> HandleAsync(
		Guid tenantId, string clientId, ProductClientCommand command, CancellationToken cancellationToken = default)
	{
		var validation = await validator.ValidateAsync(tenantId, command, cancellationToken).ConfigureAwait(false);

		if (validation.IsFailure)
		{
			return Result.Failure(validation.Error);
		}

		if (await store.NameExistsAsync(tenantId, validation.Value.Name, clientId, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure(SecureGateErrors.Clients.NameAlreadyExists);
		}

		return await store.UpdateAsync(tenantId, clientId, validation.Value, cancellationToken).ConfigureAwait(false)
			? Result.Success()
			: Result.Failure(SecureGateErrors.Clients.NotFound);
	}
}

/// <summary>
/// Troca o secret na hora (ADR-0037): o antigo morre, tokens já emitidos valem até expirar. POST de
/// efeito — o SDK nunca o repete, então um timeout não gera secret perdido no meio (ADR-0034).
/// </summary>
public sealed class RotateProductClientSecretHandler(IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result<ProductClientSecretDto>> HandleAsync(
		Guid tenantId, string clientId, CancellationToken cancellationToken = default)
	{
		var secret = ProductClientRules.NewSecret();

		return await store.RotateSecretAsync(tenantId, clientId, secret, cancellationToken).ConfigureAwait(false)
			? Result.Success(new ProductClientSecretDto(clientId, secret))
			: Result.Failure<ProductClientSecretDto>(SecureGateErrors.Clients.NotFound);
	}
}

/// <summary>Revoga o client removendo-o; repetir responde 404 sem outro efeito (ADR-0034).</summary>
public sealed class DeleteProductClientHandler(IProductClientStore store)
{
	/// <summary>Executa o caso de uso.</summary>
	public async Task<Result> HandleAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		await store.DeleteAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false)
			? Result.Success()
			: Result.Failure(SecureGateErrors.Clients.NotFound);
}
```

Registrar em `SecureGateApplicationExtensions.AddSecureGateApplication`, junto dos demais `AddScoped` de handler:

```csharp
		// Clients de produto (ADR-0037)
		services.AddScoped<Clients.ProductClientAccessValidator>();
		services.AddScoped<Clients.CreateProductClientHandler>();
		services.AddScoped<Clients.ListProductClientsHandler>();
		services.AddScoped<Clients.GetProductClientHandler>();
		services.AddScoped<Clients.UpdateProductClientHandler>();
		services.AddScoped<Clients.RotateProductClientSecretHandler>();
		services.AddScoped<Clients.DeleteProductClientHandler>();
```

- [ ] **Step 10: Run to verify pass**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientHandlersTests|FullyQualifiedName~ProductClientRulesTests"`
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Application tests/SecureGate/Secco.SecureGate.Tests/Unit
git commit -m "feat(securegate): casos de uso de client de produto (ADR-0037)"
```

---

### Task 5: Infrastructure — adaptador OpenIddict do store de clients de produto

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Clients/OpenIddictProductClientStore.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/SecureGateInfrastructureExtensions.cs`
- Test: coberto pelos testes de integração da Task 6 (o adaptador não tem lógica de negócio).

**Interfaces:**
- Consumes: `IProductClientStore`, `ProductClientAccess`, `ProductClientDto` (Task 4); `OidcApplication` (Task 2).
- Produces: registro `services.AddScoped<Application.Clients.IProductClientStore, Clients.OpenIddictProductClientStore>()`.

- [ ] **Step 1: Implement** — `OpenIddictProductClientStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.OpenIddict;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>
/// Clients de produto sobre o OpenIddict (ADR-0037). Toda busca filtra por tenant E por origem Api;
/// a escrita passa pelo manager do OpenIddict, que hasheia o secret e leva junto tokens e
/// autorizações na remoção.
/// </summary>
internal sealed class OpenIddictProductClientStore(
	SecureGateDbContext context,
	OpenIddictApplicationManager<OidcApplication> applications) : IProductClientStore
{
	private IQueryable<OidcApplication> TenantClients(Guid tenantId) =>
		context.Set<OidcApplication>().Where(a => a.TenantId == tenantId && a.Origin == ClientOrigin.Api);

	public Task<bool> NameExistsAsync(Guid tenantId, string name, string? exceptClientId, CancellationToken cancellationToken = default) =>
		TenantClients(tenantId).AnyAsync(a => a.Name == name && a.ClientId != exceptClientId, cancellationToken);

	public async Task<ProductClientDto> CreateAsync(
		Guid tenantId, string clientId, string clientSecret, ProductClientAccess access, CancellationToken cancellationToken = default)
	{
		// Um INSERT só, com tenant e origem já preenchidos: nunca existe, nem por um instante, um
		// client com escopo de produto e sem tenant.
		var application = new OidcApplication
		{
			TenantId = tenantId,
			Origin = ClientOrigin.Api,
			Name = access.Name,
			Roles = JoinRoles(access.Roles),
		};

		await applications.PopulateAsync(application, Describe(clientId, access), cancellationToken).ConfigureAwait(false);
		await applications.CreateAsync(application, clientSecret, cancellationToken).ConfigureAwait(false);

		return ToDto(application);
	}

	public async Task<IReadOnlyList<ProductClientDto>> ListAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
		[.. (await TenantClients(tenantId).AsNoTracking().OrderBy(a => a.Name).ToListAsync(cancellationToken).ConfigureAwait(false))
			.Select(ToDto)];

	public async Task<ProductClientDto?> GetAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default) =>
		await FindAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is { } application ? ToDto(application) : null;

	public async Task<bool> UpdateAsync(
		Guid tenantId, string clientId, ProductClientAccess access, CancellationToken cancellationToken = default)
	{
		if (await FindAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is not { } application)
		{
			return false;
		}

		// Descriptor a partir do estado atual: o secret entra já hasheado, e o manager só re-hasheia
		// quando o valor muda — alterar acesso nunca troca a credencial.
		var descriptor = new OpenIddictApplicationDescriptor();
		await applications.PopulateAsync(descriptor, application, cancellationToken).ConfigureAwait(false);

		var desired = Describe(clientId, access);
		descriptor.DisplayName = desired.DisplayName;
		descriptor.Permissions.Clear();
		descriptor.Permissions.UnionWith(desired.Permissions);

		application.Name = access.Name;
		application.Roles = JoinRoles(access.Roles);

		await applications.UpdateAsync(application, descriptor, cancellationToken).ConfigureAwait(false);
		return true;
	}

	public async Task<bool> RotateSecretAsync(Guid tenantId, string clientId, string clientSecret, CancellationToken cancellationToken = default)
	{
		if (await FindAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is not { } application)
		{
			return false;
		}

		await applications.UpdateAsync(application, clientSecret, cancellationToken).ConfigureAwait(false);
		return true;
	}

	public async Task<bool> DeleteAsync(Guid tenantId, string clientId, CancellationToken cancellationToken = default)
	{
		if (await FindAsync(tenantId, clientId, cancellationToken).ConfigureAwait(false) is not { } application)
		{
			return false;
		}

		await applications.DeleteAsync(application, cancellationToken).ConfigureAwait(false);
		return true;
	}

	private Task<OidcApplication?> FindAsync(Guid tenantId, string clientId, CancellationToken cancellationToken) =>
		TenantClients(tenantId).FirstOrDefaultAsync(a => a.ClientId == clientId, cancellationToken);

	private static OpenIddictApplicationDescriptor Describe(string clientId, ProductClientAccess access)
	{
		var descriptor = new OpenIddictApplicationDescriptor
		{
			ClientId = clientId,
			ClientType = ClientTypes.Confidential,
			DisplayName = access.Name,
		};
		descriptor.Permissions.Add(Permissions.Endpoints.Token);
		descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);

		foreach (var scope in access.Scopes)
		{
			descriptor.Permissions.Add(Permissions.Prefixes.Scope + scope);
		}

		return descriptor;
	}

	private static string? JoinRoles(IReadOnlyList<string> roles) => roles.Count == 0 ? null : string.Join(' ', roles);

	private static ProductClientDto ToDto(OidcApplication application) =>
		new(
			application.ClientId!,
			application.Name!,
			[.. ParsePermissions(application.Permissions)
				.Where(p => p.StartsWith(Permissions.Prefixes.Scope, StringComparison.Ordinal))
				.Select(p => p[Permissions.Prefixes.Scope.Length..])
				.Order(StringComparer.Ordinal)],
			application.Roles?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [],
			application.CreationDate is { } created ? new DateTimeOffset(created, TimeSpan.Zero) : null);

	private static IEnumerable<string> ParsePermissions(string? json) =>
		string.IsNullOrEmpty(json) ? [] : System.Text.Json.JsonSerializer.Deserialize<string[]>(json) ?? [];
}
```

Observação para o executor: se `OidcApplication` não tiver `CreationDate` (o tipo do OpenIddict EF Core não tem por padrão), trocar o último argumento de `ToDto` por `null` e remover `CreatedAt` do teste da Task 6 — não acrescentar coluna nova por isso.

- [ ] **Step 2: Register** — em `AddSecureGateInfrastructure`, depois do registro de `IGroupRoleMappingRepository`:

```csharp
		// Clients de produto vinculados a tenant (ADR-0037)
		services.AddScoped<Application.Clients.IProductClientStore, Clients.OpenIddictProductClientStore>();
```

- [ ] **Step 3: Build**

Run: `dotnet build src/SecureGate/Secco.SecureGate.Api/Secco.SecureGate.Api.csproj --configuration Release`
Expected: build sem erro nem warning.

- [ ] **Step 4: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Infrastructure
git commit -m "feat(securegate): store OpenIddict de clients de produto (ADR-0037)"
```

---

### Task 6: API de clients de produto

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Api/Endpoints/ProductClientEndpoints.cs`
- Create: `src/SecureGate/Secco.SecureGate.Api/Requests/ProductClientRequests.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Program.cs` (`app.MapProductClientEndpoints();` depois de `app.MapEntraGroupsEndpoints();`)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/ProductClientApiTests.cs`

**Interfaces:**
- Consumes: handlers da Task 4; `IdentitySeed.AdminClient/TenantAsync/RoleAsync` (existentes); `factory.CreateClientAsync` (existente).
- Produces: rotas `/api/v1/tenants/{tenantId:guid}/clients[...]` com os `operationId` das Global Constraints; `ProductClientRequest(string? Name, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? Roles)`.

- [ ] **Step 1: Write the failing tests** — `ProductClientApiTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Gestão de client de produto pela API (ADR-0037).</summary>
[Collection(SharedApiCollectionDefinition.Name)]
public class ProductClientApiTests(SecureGateApiFactory factory) : IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantId;
	private HttpClient _admin = null!;

	public async Task InitializeAsync()
	{
		await factory.EnsureDatabaseMigratedAsync();
		_tenantId = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, _tenantId, "compras-writer", "log-entries:write");
		_admin = IdentitySeed.AdminClient(factory);
	}

	public Task DisposeAsync() => Task.CompletedTask;

	private string Clients => $"/api/v1/tenants/{_tenantId}/clients";

	private async Task<JsonElement> CreateAsync(string name, string[]? scopes = null, string[]? roles = null)
	{
		var response = await _admin.PostAsJsonAsync(Clients, new { name, scopes = scopes ?? ["logstream"], roles = roles ?? [] });
		response.StatusCode.Should().Be(HttpStatusCode.Created);
		return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
	}

	private Task<HttpResponseMessage> TokenAsync(string clientId, string secret) =>
		factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = secret,
			["scope"] = "logstream",
		}));

	[Fact]
	public async Task Create_Valido_SecretFuncionaETokenSaiNoTenant()
	{
		var created = await CreateAsync("Compras", roles: ["COMPRAS-WRITER"]);
		var clientId = created.GetProperty("clientId").GetString()!;

		created.GetProperty("roles").EnumerateArray().Select(r => r.GetString())
			.Should().Equal(["compras-writer"], "grava o nome canônico do tb_roles");

		var token = await TokenAsync(clientId, created.GetProperty("clientSecret").GetString()!);
		token.StatusCode.Should().Be(HttpStatusCode.OK);
		var jwt = new JsonWebTokenHandler().ReadJsonWebToken(
			(await token.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString());
		jwt.GetClaim("tenant_id").Value.Should().Be(_tenantId.ToString());
	}

	[Fact]
	public async Task GetEList_NuncaDevolvemSecret()
	{
		var created = await CreateAsync($"Sem segredo {Guid.NewGuid():N}");
		var clientId = created.GetProperty("clientId").GetString()!;

		(await _admin.GetStringAsync($"{Clients}/{clientId}")).Should().NotContain("clientSecret");
		(await _admin.GetStringAsync(Clients)).Should().NotContain("clientSecret");
	}

	[Theory]
	[InlineData("securegate:admin")]
	[InlineData("authorization:read")]
	[InlineData("catalog:logstream")]
	[InlineData("securegate")]
	public async Task Create_EscopoDeInfraestrutura_400(string scope)
	{
		var response = await _admin.PostAsJsonAsync(Clients, new { name = "x", scopes = new[] { scope }, roles = Array.Empty<string>() });

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
	}

	[Fact]
	public async Task Create_NomeDuplicado_409()
	{
		var name = $"Duplicado {Guid.NewGuid():N}";
		await CreateAsync(name);

		var response = await _admin.PostAsJsonAsync(Clients, new { name, scopes = new[] { "logstream" }, roles = Array.Empty<string>() });

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
	}

	[Fact]
	public async Task Update_RepetidoComMesmoNome_204DuasVezesEEstadoIgual()
	{
		var name = $"Repetido {Guid.NewGuid():N}";
		var clientId = (await CreateAsync(name)).GetProperty("clientId").GetString()!;
		var body = new { name, scopes = new[] { "logstream", "notificationhub" }, roles = new[] { "compras-writer" } };

		(await _admin.PutAsJsonAsync($"{Clients}/{clientId}", body)).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await _admin.PutAsJsonAsync($"{Clients}/{clientId}", body)).StatusCode.Should().Be(HttpStatusCode.NoContent);

		var client = await _admin.GetFromJsonAsync<JsonElement>($"{Clients}/{clientId}", Json);
		client.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()).Should().Equal("logstream", "notificationhub");
		client.GetProperty("roles").EnumerateArray().Select(s => s.GetString()).Should().Equal("compras-writer");
	}

	[Fact]
	public async Task Rotate_SecretAntigoMorreNovoFunciona()
	{
		var created = await CreateAsync($"Rotacao {Guid.NewGuid():N}");
		var clientId = created.GetProperty("clientId").GetString()!;
		var oldSecret = created.GetProperty("clientSecret").GetString()!;

		var rotated = await (await _admin.PostAsync($"{Clients}/{clientId}/rotate-secret", null))
			.Content.ReadFromJsonAsync<JsonElement>(Json);
		var newSecret = rotated.GetProperty("clientSecret").GetString()!;

		newSecret.Should().NotBe(oldSecret);
		(await TokenAsync(clientId, oldSecret)).IsSuccessStatusCode.Should().BeFalse();
		(await TokenAsync(clientId, newSecret)).StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Delete_RepetidoResponde404ESemToken()
	{
		var created = await CreateAsync($"Revogado {Guid.NewGuid():N}");
		var clientId = created.GetProperty("clientId").GetString()!;

		(await _admin.DeleteAsync($"{Clients}/{clientId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
		(await _admin.DeleteAsync($"{Clients}/{clientId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await TokenAsync(clientId, created.GetProperty("clientSecret").GetString()!)).IsSuccessStatusCode.Should().BeFalse();
	}

	[Fact]
	public async Task ClientDeOutroTenant_404EmTodasAsRotas()
	{
		var clientId = (await CreateAsync($"Alheio {Guid.NewGuid():N}")).GetProperty("clientId").GetString()!;
		var other = $"/api/v1/tenants/{await IdentitySeed.TenantAsync(factory)}/clients/{clientId}";

		await AssertNotFoundEverywhereAsync(other);
	}

	[Fact]
	public async Task ClientDePlataforma_InvisivelPelaApi()
	{
		var platformId = $"plataforma-{Guid.NewGuid():N}"[..30];
		await factory.CreateClientAsync(platformId, "plataforma-secret-de-32-chars-min!!!", "logstream");

		await AssertNotFoundEverywhereAsync($"{Clients}/{platformId}");
		(await _admin.GetStringAsync(Clients)).Should().NotContain(platformId);
	}

	[Fact]
	public async Task SemEscopoAdmin_403()
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization = new("Bearer", factory.CreateTokenWithScopes("logstream"));

		(await client.GetAsync(Clients)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	private async Task AssertNotFoundEverywhereAsync(string route)
	{
		var body = new { name = "x", scopes = new[] { "logstream" }, roles = Array.Empty<string>() };

		(await _admin.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await _admin.PutAsJsonAsync(route, body)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await _admin.PostAsync($"{route}/rotate-secret", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
		(await _admin.DeleteAsync(route)).StatusCode.Should().Be(HttpStatusCode.NotFound);
	}
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientApiTests"`
Expected: FAIL — rotas inexistentes (404 no POST de criação).

- [ ] **Step 3: Add the request type** — `ProductClientRequests.cs`:

```csharp
namespace Secco.SecureGate.Api.Requests;

/// <summary>Corpo de criação e de alteração de client de produto (ADR-0037) — conjunto completo.</summary>
/// <param name="Name">Nome legível, único no tenant.</param>
/// <param name="Scopes">Escopos de API de produto.</param>
/// <param name="Roles">Perfis do tenant.</param>
public sealed record ProductClientRequest(string? Name, IReadOnlyList<string>? Scopes, IReadOnlyList<string>? Roles);
```

- [ ] **Step 4: Add the endpoints** — `ProductClientEndpoints.cs`:

```csharp
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Clients;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Gestão de client de PRODUTO (<c>/api/v1/tenants/{tenantId}/clients</c>, ADR-0037): client
/// <c>client_credentials</c> vinculado ao tenant, cujo token sai com <c>tenant_id</c>. Não existe
/// caminho aqui para client sem tenant — esses só nascem da configuração da instalação.
/// </summary>
public static class ProductClientEndpoints
{
	/// <summary>Mapeia os endpoints de gestão de clients de produto.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapProductClientEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/clients")
			.WithTags("Clients")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

		group.MapPost("/", async (
				Guid tenantId, ProductClientRequest request, CreateProductClientHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, ToCommand(request), cancellationToken))
				.ToHttpResult(created => Results.Created($"/api/v1/tenants/{tenantId}/clients/{created.ClientId}", created)))
			.WithName("CreateProductClient")
			.WithSummary("Registra client de produto vinculado ao tenant. O secret aparece só nesta resposta.")
			.Produces<CreatedProductClientDto>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapGet("/", async (Guid tenantId, ListProductClientsHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, cancellationToken)).ToHttpResult(clients => Results.Ok(clients)))
			.WithName("ListProductClients")
			.WithSummary("Lista os clients de produto do tenant (sem secret).")
			.Produces<IReadOnlyList<ProductClientDto>>(StatusCodes.Status200OK);

		group.MapGet("/{clientId}", async (
				Guid tenantId, string clientId, GetProductClientHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, clientId, cancellationToken)).ToHttpResult(client => Results.Ok(client)))
			.WithName("GetProductClient")
			.WithSummary("Detalha um client de produto do tenant (sem secret).")
			.Produces<ProductClientDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/{clientId}", async (
				Guid tenantId, string clientId, ProductClientRequest request, UpdateProductClientHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, clientId, ToCommand(request), cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("UpdateProductClient")
			.WithSummary("Substitui nome, escopos e papéis do client (idempotente). Não altera o secret.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapPost("/{clientId}/rotate-secret", async (
				Guid tenantId, string clientId, RotateProductClientSecretHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, clientId, cancellationToken)).ToHttpResult(secret => Results.Ok(secret)))
			.WithName("RotateProductClientSecret")
			.WithSummary("Gera secret novo e invalida o anterior na hora. O secret aparece só nesta resposta.")
			.Produces<ProductClientSecretDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/{clientId}", async (
				Guid tenantId, string clientId, DeleteProductClientHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, clientId, cancellationToken)).ToHttpResult(() => Results.NoContent()))
			.WithName("DeleteProductClient")
			.WithSummary("Revoga o client removendo-o. Tokens já emitidos expiram sozinhos.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}

	private static ProductClientCommand ToCommand(ProductClientRequest request) =>
		new(request.Name, request.Scopes, request.Roles);
}
```

Em `Program.cs`, acrescentar `app.MapProductClientEndpoints();` logo após `app.MapEntraGroupsEndpoints();`.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientApiTests"`
Expected: PASS (todos). O `OpenApiContractTests` vai falhar até a Task 8 — esperado.

- [ ] **Step 6: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Api tests/SecureGate/Secco.SecureGate.Tests/Integration/ProductClientApiTests.cs
git commit -m "feat(securegate): API de gestao de clients de produto (issue #31, ADR-0037)"
```

---

### Task 7: Perfil em uso por client não pode ser excluído

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Roles/IRoleRepository.cs` (enum `DeleteRoleOutcome`)
- Modify: `src/SecureGate/Secco.SecureGate.Application/Roles/DeleteRoleHandler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Application/SecureGateErrors.cs` (`Roles.UsedByClients`)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Roles/RoleRepository.cs:160-187`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/RoleProfileManagementTests.cs` (novos testes)

**Interfaces:**
- Consumes: `factory.CreateProductClientAsync` (Task 3).
- Produces: `DeleteRoleOutcome.UsedByClients`; `SecureGateErrors.Roles.UsedByClients` (409).

- [ ] **Step 1: Write the failing tests** — acrescentar a `RoleProfileManagementTests`:

```csharp
	[Fact]
	public async Task DeleteRole_UsadoPorClientDoTenant_409()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "maquina-writer", "log-entries:write");
		await factory.CreateProductClientAsync(_tenantId, $"cli_{Guid.NewGuid():N}"[..20],
			"client-secret-de-32-chars-minimo!!!", "outro Maquina-Writer", "logstream");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/maquina-writer");

		response.StatusCode.Should().Be(HttpStatusCode.Conflict);
		(await response.Content.ReadAsStringAsync()).Should().Contain("SecureGate.Role.UsedByClients");
	}

	[Fact]
	public async Task DeleteRole_ClientComPapelDeNomeParecido_NaoBloqueia()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "tenant-admin-x");
		await factory.CreateProductClientAsync(_tenantId, $"cli_{Guid.NewGuid():N}"[..20],
			"client-secret-de-32-chars-minimo!!!", "admin-x", "logstream");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/tenant-admin-x");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent, "comparação por papel inteiro, não por substring");
	}

	[Fact]
	public async Task DeleteRole_ClientDeOutroTenantComMesmoNome_NaoBloqueia()
	{
		await IdentitySeed.RoleAsync(factory, _tenantId, "homonimo-y");
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await factory.CreateProductClientAsync(otherTenant, $"cli_{Guid.NewGuid():N}"[..20],
			"client-secret-de-32-chars-minimo!!!", "homonimo-y", "logstream");

		var response = await IdentitySeed.AdminClient(factory)
			.DeleteAsync($"/api/v1/tenants/{_tenantId}/roles/homonimo-y");

		response.StatusCode.Should().Be(HttpStatusCode.NoContent);
	}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~RoleProfileManagementTests"`
Expected: FAIL em `DeleteRole_UsadoPorClientDoTenant_409` (hoje responde 204).

- [ ] **Step 3: Implement**

Em `IRoleRepository.cs`, no enum `DeleteRoleOutcome`, depois de `HasMembers`:

```csharp
	/// <summary>Perfil usado por client de produto do tenant (ADR-0037); nada foi alterado.</summary>
	UsedByClients,
```

Em `SecureGateErrors.Roles`, depois de `HasMembers`:

```csharp
		/// <summary>
		/// Perfil usado por client de máquina do tenant (ADR-0037). Excluir e recriar o perfil com o
		/// mesmo nome devolveria acesso a um client esquecido — por isso a exclusão é recusada.
		/// </summary>
		public static readonly Error UsedByClients =
			Error.Conflict("SecureGate.Role.UsedByClients",
				"O perfil é usado por client de máquina deste tenant. Retire-o dos clients antes de excluir.");
```

Em `DeleteRoleHandler`, no `switch`, acrescentar antes do `_`:

```csharp
			DeleteRoleOutcome.UsedByClients => Result.Failure(SecureGateErrors.Roles.UsedByClients),
```

Em `RoleRepository.DeleteRoleAsync`, logo depois do bloco que devolve `HasMembers`:

```csharp
		// Clients de produto do tenant guardam os papéis em ds_roles (lista separada por espaço).
		// Filtro grosso no banco e comparação exata por papel em memória: "admin" não pode casar
		// com "tenant-admin" (ADR-0037). Caixa ignorada — o Identity normaliza o nome do perfil.
		var clientRoleLists = await context.Set<OpenIddict.OidcApplication>()
			.Where(a => a.TenantId == tenantId && a.Roles != null)
			.Select(a => a.Roles!)
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		if (clientRoleLists.Any(list => list
				.Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Contains(role.Name, StringComparer.OrdinalIgnoreCase)))
		{
			return DeleteRoleOutcome.UsedByClients;
		}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~RoleProfileManagementTests|FullyQualifiedName~RoleManagementTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate tests/SecureGate/Secco.SecureGate.Tests/Integration/RoleProfileManagementTests.cs
git commit -m "feat(securegate): perfil em uso por client de produto nao pode ser excluido (ADR-0037)"
```

---

### Task 8: Contrato OpenAPI e client NSwag

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Api/openapi/*.json` (snapshot regenerado)
- Modify: código gerado do `src/SecureGate/Secco.SecureGate.Client` (regenerado pelo build)
- Modify: `src/SecureGate/Secco.SecureGate.Client/README.md` (seção "Clients de produto")
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/OpenApiContractTests.cs` (existente)

- [ ] **Step 1: Confirm the contract test fails**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~OpenApiContractTests"`
Expected: FAIL em `OpenApiDocument_Always_MatchesCommittedSnapshot`.

- [ ] **Step 2: Regenerate the snapshot**

Run (bash): `SECCO_UPDATE_OPENAPI=true dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~OpenApiContractTests"`
Expected: PASS; snapshot alterado. Conferir no `git diff` que entraram exatamente os 6 `operationId` e os schemas `ProductClientRequest`, `ProductClientDto`, `CreatedProductClientDto`, `ProductClientSecretDto` — e nenhum `operationId` existente mudou.

- [ ] **Step 3: Rebuild the client**

Run: `dotnet build src/SecureGate/Secco.SecureGate.Client/Secco.SecureGate.Client.csproj --configuration Release`
Expected: build ok; métodos `CreateProductClientAsync`, `ListProductClientsAsync`, `GetProductClientAsync`, `UpdateProductClientAsync`, `RotateProductClientSecretAsync`, `DeleteProductClientAsync` presentes no código gerado.

- [ ] **Step 4: Document in the client README** — acrescentar seção:

```markdown
## Clients de produto (0.15.0, ADR-0037)

Com escopo `securegate:admin`, registre a credencial `client_credentials` de um sistema da
empresa vinculada a um tenant: `CreateProductClientAsync(tenantId, new ProductClientRequest { Name, Scopes, Roles })`.
O secret volta **só** nessa resposta e em `RotateProductClientSecretAsync` — guarde-o na hora.
O token desse client sai com `tenant_id`, então ele só alcança o próprio tenant. Escopos
aceitos: `logstream`, `notificationhub`. Clients sem tenant (de plataforma) não passam por esta
API: são declarados em `SecureGate:PlatformClients` na configuração do SecureGate.
```

- [ ] **Step 5: Run full SecureGate suite**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/SecureGate
git commit -m "feat(securegate): contrato e client NSwag de clients de produto (ADR-0037)"
```

---

### Task 9: Options dos clients de plataforma com validação fail-fast

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Clients/PlatformClientsOptions.cs`
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Clients/PlatformClientsOptionsValidator.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/SecureGateInfrastructureExtensions.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/PlatformClientsOptionsValidatorTests.cs`

**Interfaces:**
- Produces: `PlatformClientsOptions { const string SectionKey = "SecureGate"; List<PlatformClientDefinition> PlatformClients }`, `PlatformClientDefinition { string? ClientId; PlatformClientType? Type; string? ClientSecret; List<string> Scopes; List<string> Roles; List<string> RedirectUris; List<string> PostLogoutRedirectUris }`, `enum PlatformClientType { ClientCredentials, AuthorizationCode }`, `PlatformClientsOptionsValidator(IHostEnvironment)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Secco.SecureGate.Infrastructure.Clients;
using Xunit;

namespace Secco.SecureGate.Tests.Unit;

public class PlatformClientsOptionsValidatorTests
{
	private const string GoodSecret = "segredo-de-plataforma-com-32-chars!!";

	private static PlatformClientsOptionsValidator Validator(string environment)
	{
		var host = Substitute.For<IHostEnvironment>();
		host.EnvironmentName.Returns(environment);
		return new PlatformClientsOptionsValidator(host);
	}

	private static PlatformClientsOptions With(params PlatformClientDefinition[] clients) =>
		new() { PlatformClients = [.. clients] };

	private static PlatformClientDefinition Machine(string clientId = "secco-logstream", string? secret = GoodSecret) =>
		new() { ClientId = clientId, Type = PlatformClientType.ClientCredentials, ClientSecret = secret, Scopes = ["catalog:logstream"] };

	[Fact]
	public void Validate_SemClients_Sucesso() =>
		Validator(Environments.Production).Validate(null, With()).Succeeded.Should().BeTrue();

	[Fact]
	public void Validate_ClientValido_Sucesso() =>
		Validator(Environments.Production).Validate(null, With(Machine())).Succeeded.Should().BeTrue();

	[Theory]
	[InlineData("cli_abc")]
	[InlineData("Maiuscula")]
	[InlineData("com espaco")]
	[InlineData("")]
	public void Validate_ClientIdInvalido_Falha(string clientId) =>
		Validator(Environments.Production).Validate(null, With(Machine(clientId))).Failed.Should().BeTrue();

	[Fact]
	public void Validate_ClientIdRepetido_Falha() =>
		Validator(Environments.Production).Validate(null, With(Machine(), Machine())).Failed.Should().BeTrue();

	[Fact]
	public void Validate_SecretCurtoForaDeDev_Falha() =>
		Validator(Environments.Production).Validate(null, With(Machine(secret: "curto"))).Failed.Should().BeTrue();

	[Fact]
	public void Validate_SecretCurtoEmDev_Sucesso() =>
		Validator(Environments.Development).Validate(null, With(Machine(secret: "curto"))).Succeeded.Should().BeTrue();

	[Fact]
	public void Validate_SecretAusente_FalhaEmQualquerAmbiente() =>
		Validator(Environments.Development).Validate(null, With(Machine(secret: null))).Failed.Should().BeTrue();

	[Fact]
	public void Validate_TipoAusente_Falha() =>
		Validator(Environments.Production).Validate(null, With(new PlatformClientDefinition
		{
			ClientId = "x", ClientSecret = GoodSecret, Scopes = ["logstream"],
		})).Failed.Should().BeTrue();

	[Fact]
	public void Validate_AuthorizationCodeSemRedirect_Falha() =>
		Validator(Environments.Production).Validate(null, With(new PlatformClientDefinition
		{
			ClientId = "secco-adminportal", Type = PlatformClientType.AuthorizationCode, ClientSecret = GoodSecret,
			Scopes = ["openid"],
		})).Failed.Should().BeTrue();

	[Fact]
	public void Validate_RedirectHttpForaDeDev_Falha() =>
		Validator(Environments.Production).Validate(null, With(new PlatformClientDefinition
		{
			ClientId = "secco-adminportal", Type = PlatformClientType.AuthorizationCode, ClientSecret = GoodSecret,
			Scopes = ["openid"], RedirectUris = ["http://portal.empresa.local/signin-oidc"],
		})).Failed.Should().BeTrue();

	[Fact]
	public void Validate_MensagemDeFalha_NuncaContemOSecret()
	{
		var result = Validator(Environments.Production).Validate(null, With(Machine(clientId: "cli_x", secret: GoodSecret)));

		result.FailureMessage.Should().NotContain(GoodSecret);
	}
}
```

- [ ] **Step 2: Run to verify fail**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~PlatformClientsOptionsValidatorTests"`
Expected: FAIL de compilação.

- [ ] **Step 3: Implement** — `PlatformClientsOptions.cs`:

```csharp
namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>Tipo de client de plataforma declarável (ADR-0037). Client público não existe nesta versão.</summary>
public enum PlatformClientType
{
	/// <summary>Máquina: grant client credentials.</summary>
	ClientCredentials,

	/// <summary>Relying party confidencial: authorization code + PKCE + refresh, consent implícito.</summary>
	AuthorizationCode,
}

/// <summary>Um client de plataforma declarado na configuração (ADR-0037).</summary>
public sealed class PlatformClientDefinition
{
	/// <summary>Identificador kebab-case; nunca com o prefixo <c>cli_</c> da API.</summary>
	public string? ClientId { get; set; }

	/// <summary>Tipo do client.</summary>
	public PlatformClientType? Type { get; set; }

	/// <summary>Secret — de variável de ambiente ou cofre, nunca de arquivo versionado.</summary>
	public string? ClientSecret { get; set; }

	/// <summary>Escopos concedidos (precisam estar registrados).</summary>
	public List<string> Scopes { get; set; } = [];

	/// <summary>Papéis carregados na claim <c>role</c>.</summary>
	public List<string> Roles { get; set; } = [];

	/// <summary>Redirect URIs (só <see cref="PlatformClientType.AuthorizationCode"/>).</summary>
	public List<string> RedirectUris { get; set; } = [];

	/// <summary>Post-logout redirect URIs (só <see cref="PlatformClientType.AuthorizationCode"/>).</summary>
	public List<string> PostLogoutRedirectUris { get; set; } = [];
}

/// <summary>
/// Clients de plataforma (seção <c>SecureGate:PlatformClients</c>, ADR-0037): a configuração é a
/// fonte da verdade, reconciliada pelo seed de referência.
/// </summary>
public sealed class PlatformClientsOptions
{
	/// <summary>Seção que contém a lista <c>PlatformClients</c>.</summary>
	public const string SectionKey = "SecureGate";

	/// <summary>Clients declarados.</summary>
	public List<PlatformClientDefinition> PlatformClients { get; set; } = [];
}
```

`PlatformClientsOptionsValidator.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Secco.SecureGate.Application.Clients;

namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>
/// Validação fail-fast de <c>SecureGate:PlatformClients</c> (ADR-0037) — roda em toda subida, em
/// qualquer ambiente, mesmo onde o seed não roda. A mensagem nomeia índice e campo, nunca o secret.
/// </summary>
internal sealed partial class PlatformClientsOptionsValidator(IHostEnvironment environment)
	: IValidateOptions<PlatformClientsOptions>
{
	private const int ClientIdMaxLength = 100;
	private const int MinimumSecretLength = 32;

	[GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
	private static partial Regex ClientIdPattern();

	public ValidateOptionsResult Validate(string? name, PlatformClientsOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var failures = new List<string>();
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var development = environment.IsDevelopment();

		for (var i = 0; i < options.PlatformClients.Count; i++)
		{
			var client = options.PlatformClients[i];
			var at = $"SecureGate:PlatformClients:{i}";

			if (string.IsNullOrEmpty(client.ClientId) || client.ClientId.Length > ClientIdMaxLength
				|| !ClientIdPattern().IsMatch(client.ClientId))
			{
				failures.Add($"{at}:ClientId deve ser kebab-case minúsculo com até {ClientIdMaxLength} caracteres.");
			}
			else if (client.ClientId.StartsWith(ProductClientRules.ClientIdPrefix, StringComparison.Ordinal))
			{
				failures.Add($"{at}:ClientId não pode começar com '{ProductClientRules.ClientIdPrefix}', reservado à API.");
			}
			else if (!seen.Add(client.ClientId))
			{
				failures.Add($"{at}:ClientId '{client.ClientId}' repetido.");
			}

			if (client.Type is null)
			{
				failures.Add($"{at}:Type é obrigatório (ClientCredentials ou AuthorizationCode).");
			}

			if (string.IsNullOrEmpty(client.ClientSecret))
			{
				failures.Add($"{at}:ClientSecret é obrigatório.");
			}
			else if (!development && client.ClientSecret.Length < MinimumSecretLength)
			{
				failures.Add($"{at}:ClientSecret precisa de ao menos {MinimumSecretLength} caracteres fora de Development.");
			}

			if (client.Type == PlatformClientType.AuthorizationCode)
			{
				if (client.RedirectUris.Count == 0)
				{
					failures.Add($"{at}:RedirectUris é obrigatório para AuthorizationCode.");
				}

				foreach (var uri in client.RedirectUris.Concat(client.PostLogoutRedirectUris))
				{
					if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
						|| (!development && parsed.Scheme != Uri.UriSchemeHttps))
					{
						failures.Add($"{at}: URI '{uri}' inválida — absoluta e, fora de Development, https.");
					}
				}
			}
		}

		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}
}
```

Registro em `AddSecureGateInfrastructure`, junto das demais options:

```csharp
		// Clients de plataforma (ADR-0037): validados em toda subida, reconciliados no seed de referência
		services.AddOptions<Clients.PlatformClientsOptions>()
			.BindConfiguration(Clients.PlatformClientsOptions.SectionKey)
			.ValidateOnStart();
		services.TryAddSingleton<IValidateOptions<Clients.PlatformClientsOptions>, Clients.PlatformClientsOptionsValidator>();
```

Nota: a checagem "escopo registrado" fica na reconciliação (Task 10), porque depende do banco; o validator de options não faz I/O.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~PlatformClientsOptionsValidatorTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Infrastructure tests/SecureGate/Secco.SecureGate.Tests/Unit/PlatformClientsOptionsValidatorTests.cs
git commit -m "feat(securegate): options de clients de plataforma com validacao fail-fast (ADR-0037)"
```

---

### Task 10: Reconciliação dos clients de plataforma no seed de referência

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Clients/PlatformClientReconciler.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/SecureGateInfrastructureExtensions.cs`
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/TestCollections.cs` (collection nova)
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/ConnectionStringEncryptionTests.cs` e `OperatorRoleConvergenceTests.cs` (mudam de collection)
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Integration/PlatformClientsSecureGateApiFactory.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/PlatformClientReconciliationTests.cs`

**Interfaces:**
- Consumes: `PlatformClientsOptions` (Task 9), `OidcApplication.Origin/Name` (Task 2).
- Produces: `PlatformClientReconciler : IReferenceDataSeeder` com `Order => 50`.

- [ ] **Step 1: Isolar os testes que re-executam o seed** — a reconciliação remove client de origem `Configuration` não declarado, e os helpers de teste criam clients com essa origem. Dois testes da collection compartilhada chamam `SeedSeccoDataAsync()` de novo e apagariam clients de outras classes. Em `TestCollections.cs`, acrescentar:

```csharp
/// <summary>
/// Testes que RE-EXECUTAM o seed de referência. Isolados da collection compartilhada: desde a
/// ADR-0037 o seed reconcilia os clients de plataforma e remove os não declarados — inclusive os
/// que os helpers de teste criam para as outras classes.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ReseedApiCollectionDefinition : ICollectionFixture<SecureGateApiFactory>
{
	/// <summary>Nome da collection.</summary>
	public const string Name = "SecureGate com seed re-executado";
}
```

Em `ConnectionStringEncryptionTests.cs` e `OperatorRoleConvergenceTests.cs`, trocar `[Collection(SharedApiCollectionDefinition.Name)]` por `[Collection(ReseedApiCollectionDefinition.Name)]`.

- [ ] **Step 2: Create the factory** — `PlatformClientsSecureGateApiFactory.cs`:

```csharp
namespace Secco.SecureGate.Tests.Integration;

/// <summary>SecureGate com dois clients de plataforma declarados (ADR-0037).</summary>
public sealed class PlatformClientsSecureGateApiFactory : SecureGateApiFactory
{
	public const string MachineId = "secco-teste-maquina";
	public const string MachineSecret = "maquina-de-plataforma-secret-32-chars!";
	public const string PortalId = "secco-teste-portal";
	public const string PortalSecret = "portal-de-plataforma-secret-32-chars!!";

	/// <inheritdoc />
	protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
	{
		base.ConfigureTestConfiguration(settings);
		ArgumentNullException.ThrowIfNull(settings);

		settings["SecureGate:PlatformClients:0:ClientId"] = MachineId;
		settings["SecureGate:PlatformClients:0:Type"] = "ClientCredentials";
		settings["SecureGate:PlatformClients:0:ClientSecret"] = MachineSecret;
		settings["SecureGate:PlatformClients:0:Scopes:0"] = "catalog:logstream";
		settings["SecureGate:PlatformClients:0:Roles:0"] = "leitor";

		settings["SecureGate:PlatformClients:1:ClientId"] = PortalId;
		settings["SecureGate:PlatformClients:1:Type"] = "AuthorizationCode";
		settings["SecureGate:PlatformClients:1:ClientSecret"] = PortalSecret;
		settings["SecureGate:PlatformClients:1:Scopes:0"] = "logstream";
		settings["SecureGate:PlatformClients:1:RedirectUris:0"] = "https://portal.testes.local/signin-oidc";
		settings["SecureGate:PlatformClients:1:PostLogoutRedirectUris:0"] = "https://portal.testes.local/signout-callback-oidc";
	}
}
```

- [ ] **Step 3: Write the failing tests** — `PlatformClientReconciliationTests.cs`:

```csharp
using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.OpenIddict;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>Reconciliação de clients de plataforma no seed de referência (ADR-0037 + emenda).</summary>
public class PlatformClientReconciliationTests(PlatformClientsSecureGateApiFactory factory)
	: IClassFixture<PlatformClientsSecureGateApiFactory>, IAsyncLifetime
{
	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private Task<HttpResponseMessage> TokenAsync(string clientId, string secret, string scope) =>
		factory.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
		{
			["grant_type"] = "client_credentials",
			["client_id"] = clientId,
			["client_secret"] = secret,
			["scope"] = scope,
		}));

	private async Task<OidcApplication?> FindAsync(string clientId)
	{
		using var scope = factory.Services.CreateScope();
		var context = scope.ServiceProvider.GetRequiredService<SecureGateDbContext>();
		return await context.Set<OidcApplication>().AsNoTracking().FirstOrDefaultAsync(a => a.ClientId == clientId);
	}

	[Fact]
	public async Task Seed_ClientDeclarado_ExisteComOrigemConfigurationEObtemToken()
	{
		var machine = await FindAsync(PlatformClientsSecureGateApiFactory.MachineId);

		machine.Should().NotBeNull();
		machine!.Origin.Should().Be(ClientOrigin.Configuration);
		machine.TenantId.Should().BeNull();
		machine.Name.Should().Be(PlatformClientsSecureGateApiFactory.MachineId);
		machine.Roles.Should().Be("leitor");

		(await TokenAsync(PlatformClientsSecureGateApiFactory.MachineId,
			PlatformClientsSecureGateApiFactory.MachineSecret, "catalog:logstream"))
			.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Seed_AuthorizationCode_ExigePkceETemRedirect()
	{
		var portal = await FindAsync(PlatformClientsSecureGateApiFactory.PortalId);

		portal!.RedirectUris.Should().Contain("https://portal.testes.local/signin-oidc");
		portal.Requirements.Should().Contain(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);
	}

	[Fact]
	public async Task Seed_ConfigurationNaoDeclarado_EhRemovidoEApiPreservado()
	{
		await factory.CreateClientAsync("orfao-de-configuracao", "orfao-secret-de-32-chars-minimo!!!", "logstream");
		var tenantId = await IdentitySeed.TenantAsync(factory);
		var apiClientId = $"cli_{Guid.NewGuid():N}"[..20];
		await factory.CreateProductClientAsync(tenantId, apiClientId, "api-secret-de-32-chars-minimo!!!!!", null, "logstream");

		await factory.Services.SeedSeccoDataAsync();

		(await FindAsync("orfao-de-configuracao")).Should().BeNull();
		(await FindAsync(apiClientId)).Should().NotBeNull("a reconciliação nunca toca client da API");
	}

	[Fact]
	public async Task Seed_DuasVezesSemMudanca_NaoReHasheiaOSecret()
	{
		var before = (await FindAsync(PlatformClientsSecureGateApiFactory.MachineId))!.ClientSecret;

		await factory.Services.SeedSeccoDataAsync();

		(await FindAsync(PlatformClientsSecureGateApiFactory.MachineId))!.ClientSecret.Should().Be(before);
	}

	[Fact]
	public async Task Seed_SecretAlteradoNoBanco_VoltaAoDaConfiguracao()
	{
		using (var scope = factory.Services.CreateScope())
		{
			var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
			var app = await manager.FindByClientIdAsync(PlatformClientsSecureGateApiFactory.MachineId);
			await manager.UpdateAsync(app!, "outro-secret-qualquer-de-32-chars!!!");
		}

		await factory.Services.SeedSeccoDataAsync();

		(await TokenAsync(PlatformClientsSecureGateApiFactory.MachineId,
			PlatformClientsSecureGateApiFactory.MachineSecret, "catalog:logstream"))
			.StatusCode.Should().Be(HttpStatusCode.OK);
	}
}
```

Teste do startup com configuração inválida — no mesmo arquivo:

```csharp
public class PlatformClientInvalidConfigurationTests
{
	private sealed class InvalidFactory : SecureGateApiFactory
	{
		protected override void ConfigureTestConfiguration(IDictionary<string, string?> settings)
		{
			base.ConfigureTestConfiguration(settings);
			settings["SecureGate:PlatformClients:0:ClientId"] = "cli_invasor";
			settings["SecureGate:PlatformClients:0:Type"] = "ClientCredentials";
			settings["SecureGate:PlatformClients:0:ClientSecret"] = "segredo-de-plataforma-com-32-chars!!";
		}
	}

	[Fact]
	public async Task Startup_ClientIdComPrefixoDaApi_Falha()
	{
		await using var factory = new InvalidFactory();

		var act = () => factory.CreateClient();

		act.Should().Throw<Microsoft.Extensions.Options.OptionsValidationException>();
		await Task.CompletedTask;
	}
}
```

Se a base `SeccoApiFactory` subir o container de banco no construtor/`InitializeAsync` e isso impedir o `CreateClient()` direto, chamar `await factory.InitializeAsync()` antes (ver `src/SDK/Secco.SDK.Testing/SeccoApiFactory.cs`) e manter a asserção sobre `OptionsValidationException`.

- [ ] **Step 4: Run to verify fail**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~PlatformClient"`
Expected: FAIL — clients declarados não existem; órfão não é removido. (O teste de configuração inválida já passa pela Task 9.)

- [ ] **Step 5: Implement** — `PlatformClientReconciler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Core;
using Secco.SDK.EntityFrameworkCore.Seeding;
using Secco.SecureGate.Application.Clients;
using Secco.SecureGate.Infrastructure.Contexts;
using Secco.SecureGate.Infrastructure.OpenIddict;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Secco.SecureGate.Infrastructure.Clients;

/// <summary>
/// Reconcilia os clients de PLATAFORMA com <c>SecureGate:PlatformClients</c> (ADR-0037 + emenda): passo
/// do seed de referência, roda onde ele roda — automático em Development, no processo controlado fora
/// dele (ADR-0005). Cria ou atualiza o declarado e remove client de origem Configuration que saiu da
/// configuração. Nunca toca client da API. Nunca loga secret.
/// </summary>
internal sealed partial class PlatformClientReconciler(
	IOptions<PlatformClientsOptions> options,
	SecureGateDbContext context,
	OpenIddictApplicationManager<OidcApplication> applications,
	IOpenIddictScopeManager scopes,
	ILogger<PlatformClientReconciler> logger) : IReferenceDataSeeder
{
	/// <summary>Depois do registro de escopos (0), antes da re-cifragem do catálogo (100).</summary>
	public int Order => 50;

	public async Task SeedAsync(CancellationToken cancellationToken = default)
	{
		var declared = options.Value.PlatformClients;
		int created = 0, updated = 0;

		foreach (var definition in declared)
		{
			await EnsureScopesRegisteredAsync(definition, cancellationToken).ConfigureAwait(false);

			var existing = await context.Set<OidcApplication>()
				.FirstOrDefaultAsync(a => a.ClientId == definition.ClientId, cancellationToken).ConfigureAwait(false);

			if (existing is null)
			{
				var application = new OidcApplication
				{
					Origin = ClientOrigin.Configuration,
					Name = definition.ClientId,
					Roles = JoinRoles(definition.Roles),
				};

				await applications.PopulateAsync(application, Describe(definition), cancellationToken).ConfigureAwait(false);

				try
				{
					await applications.CreateAsync(application, definition.ClientSecret!, cancellationToken).ConfigureAwait(false);
					created++;
				}
				catch (DbUpdateException)
				{
					// Outra instância criou no meio (ADR-0035): recarrega e segue como atualização
					context.ChangeTracker.Clear();
					existing = await context.Set<OidcApplication>()
						.FirstAsync(a => a.ClientId == definition.ClientId, cancellationToken).ConfigureAwait(false);
				}
			}

			if (existing is not null)
			{
				if (existing.Origin != ClientOrigin.Configuration)
				{
					// Inalcançável pelo prefixo cli_ proibido na configuração — defesa em profundidade
					throw new InvalidOperationException(
						$"Client de plataforma '{definition.ClientId}' colide com um client registrado pela API.");
				}

				await UpdateAsync(existing, definition, cancellationToken).ConfigureAwait(false);
				updated++;
			}
		}

		var declaredIds = declared.Select(d => d.ClientId!).ToHashSet(StringComparer.Ordinal);
		var stale = await context.Set<OidcApplication>()
			.Where(a => a.Origin == ClientOrigin.Configuration && !declaredIds.Contains(a.ClientId!))
			.ToListAsync(cancellationToken).ConfigureAwait(false);

		foreach (var application in stale)
		{
			await applications.DeleteAsync(application, cancellationToken).ConfigureAwait(false);
		}

		LogReconciled(logger, created, updated, stale.Count, string.Join(", ", stale.Select(a => a.ClientId)));
	}

	private async Task UpdateAsync(OidcApplication application, PlatformClientDefinition definition, CancellationToken cancellationToken)
	{
		// Descriptor a partir do estado atual (secret já hasheado): o manager só re-hasheia quando o
		// valor muda — e ele só muda quando o secret declarado não confere mais.
		var descriptor = new OpenIddictApplicationDescriptor();
		await applications.PopulateAsync(descriptor, application, cancellationToken).ConfigureAwait(false);

		var desired = Describe(definition);
		descriptor.DisplayName = desired.DisplayName;
		descriptor.ClientType = desired.ClientType;
		descriptor.ConsentType = desired.ConsentType;
		descriptor.Permissions.Clear();
		descriptor.Permissions.UnionWith(desired.Permissions);
		descriptor.Requirements.Clear();
		descriptor.Requirements.UnionWith(desired.Requirements);
		descriptor.RedirectUris.Clear();
		descriptor.RedirectUris.UnionWith(desired.RedirectUris);
		descriptor.PostLogoutRedirectUris.Clear();
		descriptor.PostLogoutRedirectUris.UnionWith(desired.PostLogoutRedirectUris);

		if (!await applications.ValidateClientSecretAsync(application, definition.ClientSecret!, cancellationToken)
				.ConfigureAwait(false))
		{
			descriptor.ClientSecret = definition.ClientSecret;
		}

		application.Name = definition.ClientId;
		application.Roles = JoinRoles(definition.Roles);

		await applications.UpdateAsync(application, descriptor, cancellationToken).ConfigureAwait(false);
	}

	private async Task EnsureScopesRegisteredAsync(PlatformClientDefinition definition, CancellationToken cancellationToken)
	{
		foreach (var scope in definition.Scopes.Where(s => s is not (Scopes.OpenId or Scopes.Profile or Scopes.Email or Scopes.Roles or Scopes.OfflineAccess)))
		{
			if (await scopes.FindByNameAsync(scope, cancellationToken).ConfigureAwait(false) is null)
			{
				throw new InvalidOperationException(
					$"Client de plataforma '{definition.ClientId}' declara o escopo não registrado '{scope}'.");
			}
		}
	}

	private static OpenIddictApplicationDescriptor Describe(PlatformClientDefinition definition)
	{
		var descriptor = new OpenIddictApplicationDescriptor
		{
			ClientId = definition.ClientId,
			ClientType = ClientTypes.Confidential,
			DisplayName = definition.ClientId,
		};

		descriptor.Permissions.Add(Permissions.Endpoints.Token);

		if (definition.Type == PlatformClientType.AuthorizationCode)
		{
			descriptor.ConsentType = ConsentTypes.Implicit;
			descriptor.Permissions.UnionWith(
			[
				Permissions.Endpoints.Authorization,
				Permissions.Endpoints.EndSession,
				Permissions.GrantTypes.AuthorizationCode,
				Permissions.GrantTypes.RefreshToken,
				Permissions.ResponseTypes.Code,
			]);
			descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
			descriptor.RedirectUris.UnionWith(definition.RedirectUris.Select(uri => new Uri(uri)));
			descriptor.PostLogoutRedirectUris.UnionWith(definition.PostLogoutRedirectUris.Select(uri => new Uri(uri)));
		}
		else
		{
			descriptor.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
		}

		foreach (var scope in definition.Scopes)
		{
			descriptor.Permissions.Add(scope switch
			{
				Scopes.Email => Permissions.Scopes.Email,
				Scopes.Profile => Permissions.Scopes.Profile,
				Scopes.Roles => Permissions.Scopes.Roles,
				_ => Permissions.Prefixes.Scope + scope,
			});
		}

		return descriptor;
	}

	private static string? JoinRoles(List<string> roles) => roles.Count == 0 ? null : string.Join(' ', roles);

	[LoggerMessage(Level = LogLevel.Information,
		Message = "Clients de plataforma reconciliados: {Created} criados, {Updated} conferidos/atualizados, {Removed} removidos ({RemovedIds}).")]
	private static partial void LogReconciled(ILogger logger, int created, int updated, int removed, string removedIds);
}
```

Observação: `openid` não é escopo registrado no gerenciador nem vira permissão `scp:` no OpenIddict — por isso o `switch` não o mapeia para permissão e o `EnsureScopesRegisteredAsync` o ignora. Se o `PopulateAsync(descriptor, application)` do OpenIddict exigir `ApplicationType`, copiar o valor existente (já vem do estado atual).

Registro em `AddSecureGateInfrastructure`, junto dos seeders:

```csharp
		// Clients de plataforma (ADR-0037 + emenda): reconciliados no seed de referência
		services.AddScoped<IReferenceDataSeeder, Clients.PlatformClientReconciler>();
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~PlatformClient|FullyQualifiedName~ConnectionStringEncryptionTests|FullyQualifiedName~OperatorRoleConvergenceTests"`
Expected: PASS.

- [ ] **Step 7: Run the whole SecureGate suite** (detectar client de teste apagado por reconciliação)

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/SecureGate/Secco.SecureGate.Infrastructure tests/SecureGate/Secco.SecureGate.Tests/Integration
git commit -m "feat(securegate): reconciliacao de clients de plataforma no seed de referencia (ADR-0037)"
```

---

### Task 11: DEV — clients saem do seeder e vão para a configuração

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Seeding/SecureGateDevelopmentDataSeeder.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/appsettings.Development.json`
- Modify: `docs/testing-guide.md:160-166`
- Modify: `docker-compose.yml` (só se o SecureGate rodar fora de Development no compose — conferir no Step 4)

- [ ] **Step 1: Remove clients from the DEV seeder** — em `SecureGateDevelopmentDataSeeder`:
  - apagar o bloco que cria `DevClientId` (linhas do `if (await applicationManager.FindByClientIdAsync(DevClientId ...) is null) { ... }`) e o bloco que atribui `DevRoleName` ao `devClient`;
  - apagar as chamadas `SeedWebClientAsync`, `SeedAdminPortalClientAsync`, `SeedAdminPortalSessionsClientAsync` e os três métodos;
  - apagar as constantes `DevWebClientId`, `DevClientSecret`, `AdminPortalClientSecret`, `AdminPortalSessionsClientSecret`; manter `DevClientId`, `AdminPortalClientId` e `AdminPortalSessionsClientId` só se algum teste ou código os referenciar (`grep -rn "DevClientId\|AdminPortalClientId\|AdminPortalSessionsClientId" src tests`), senão apagar também;
  - remover o parâmetro `IOpenIddictApplicationManager applicationManager` do construtor se não sobrar uso;
  - atualizar o `<summary>` da classe: "tenant demo, papel demo e usuários demo; os clients de DEV vêm de `SecureGate:PlatformClients` no `appsettings.Development.json` (ADR-0037)".

- [ ] **Step 2: Declare the DEV clients** — em `appsettings.Development.json`, dentro de `"SecureGate"`, acrescentar (mesmos ids, secrets e URIs de antes; `secco-dev-webapp` sai — era público e só aparecia no guia de testes):

```json
    "PlatformClients": [
      {
        "ClientId": "secco-dev-console",
        "Type": "ClientCredentials",
        "ClientSecret": "secco-dev-console-secret-32-chars-min!",
        "Scopes": [ "logstream", "notificationhub", "securegate", "catalog:logstream", "securegate:admin", "authorization:read" ],
        "Roles": [ "dev-admin" ]
      },
      {
        "ClientId": "secco-adminportal",
        "Type": "AuthorizationCode",
        "ClientSecret": "secco-adminportal-secret-32-chars-min!",
        "Scopes": [ "email", "profile", "roles", "securegate:admin", "logstream" ],
        "RedirectUris": [ "https://localhost:5001/signin-oidc" ],
        "PostLogoutRedirectUris": [ "https://localhost:5001/signout-callback-oidc" ]
      },
      {
        "ClientId": "secco-adminportal-sessions",
        "Type": "ClientCredentials",
        "ClientSecret": "secco-adminportal-sessions-secret-32-chars!",
        "Scopes": [ "authorization:read" ]
      }
    ],
```

- [ ] **Step 3: Update the testing guide** — em `docs/testing-guide.md`, remover a linha do `secco-dev-webapp` da tabela de credenciais e acrescentar logo abaixo da tabela:

```markdown
Os clients de DEV são declarados em `SecureGate:PlatformClients` no `appsettings.Development.json`
do SecureGate e reconciliados pelo seed de referência (ADR-0037) — o mesmo caminho de uma
instalação real. Client que sair dessa lista é removido na próxima subida.
```

- [ ] **Step 4: Check compose and launch** — Run: `grep -n "ASPNETCORE_ENVIRONMENT\|SecureGate__" docker-compose.yml .vscode/launch.json`. Se o SecureGate do compose roda em `Development`, nada muda. Se roda em outro ambiente, acrescentar ao serviço as variáveis `SecureGate__PlatformClients__N__*` equivalentes ao Step 2 e registrar no commit.

- [ ] **Step 5: Verify DEV boots** — subir infra e o SecureGate:

Run: `docker compose up -d` e depois `dotnet run --project src/SecureGate/Secco.SecureGate.Api --launch-profile https`
Expected: log "Clients de plataforma reconciliados: 3 criados..." na primeira subida (ou "0 criados, 3 conferidos" num banco de DEV existente, mais a remoção do `secco-dev-webapp`). Pedir token do console:

```bash
curl -sk https://localhost:4001/connect/token -d grant_type=client_credentials -d client_id=secco-dev-console -d client_secret=secco-dev-console-secret-32-chars-min! -d scope=logstream
```
Expected: JSON com `access_token`. Encerrar o processo.

- [ ] **Step 6: Run SecureGate suite**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/SecureGate docs/testing-guide.md docker-compose.yml .vscode/launch.json
git commit -m "refactor(securegate): clients de DEV declarados em PlatformClients, fora do seed de desenvolvimento (ADR-0037)"
```

---

### Task 12: Bateria entre produtos — LogStream e troca de token

**Files:**
- Create: `tests/SecureGate/Secco.SecureGate.Tests/Integration/ProductClientCrossProductTests.cs`

**Interfaces:**
- Consumes: `factory.CreateProductClientAsync` (Task 3); padrão do `CrossProductTokenFlowTests` (LogStream real com Authority = SecureGate) e do `TokenExchangeElevationTests` (grant de troca).

- [ ] **Step 1: Write the tests** — o `LogStreamHost` abaixo é o do `CrossProductTokenFlowTests`, com dois tenants e o papel `writer` resolvido por configuração (que não sabe de tenant — por isso o teste do papel homônimo usa o resolvedor remoto, ver Step 2):

```csharp
extern alias logstream;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Secco.LogStream.Infrastructure;
using Xunit;

namespace Secco.SecureGate.Tests.Integration;

/// <summary>
/// Client de produto contido no próprio tenant por um produto REAL (ADR-0037): o LogStream valida
/// o token do SecureGate e aplica a regra de conflito claim × header do SDK, sem mudança nenhuma.
/// </summary>
public class ProductClientCrossProductTests(SecureGateApiFactory secureGate)
	: IClassFixture<SecureGateApiFactory>, IAsyncLifetime
{
	private const string Secret = "product-client-secret-de-32-chars-min!!";
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	private Guid _tenantA;
	private Guid _tenantB;
	private string _clientId = null!;
	private LogStreamHost _logStream = null!;

	private sealed class LogStreamHost(SecureGateApiFactory secureGate, Guid tenantA, Guid tenantB)
		: WebApplicationFactory<logstream::Program>
	{
		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.UseEnvironment("Testing");

			builder.ConfigureAppConfiguration((_, configuration) =>
				configuration.AddInMemoryCollection(new Dictionary<string, string?>
				{
					["Secco:Authentication:Audience"] = "secco-logstream",
					["Secco:Authentication:Authority"] = "http://localhost",
					["Secco:Authentication:RequireHttpsMetadata"] = "false",
					[$"Secco:Tenancy:Tenants:{tenantA}:ConnectionString"] = secureGate.GetConnectionStringFor("secco_logstream_pc_a"),
					[$"Secco:Tenancy:Tenants:{tenantB}:ConnectionString"] = secureGate.GetConnectionStringFor("secco_logstream_pc_b"),
					["Secco:Authorization:Roles:writer:Permissions:0"] = "log-entries:read",
					["Secco:Authorization:Roles:writer:Permissions:1"] = "log-entries:write",
				}));

			builder.ConfigureServices(services =>
				services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
					options.BackchannelHttpHandler = secureGate.Server.CreateHandler()));
		}
	}

	public async Task InitializeAsync()
	{
		await secureGate.EnsureDatabaseMigratedAsync();
		_tenantA = await IdentitySeed.TenantAsync(secureGate);
		_tenantB = await IdentitySeed.TenantAsync(secureGate);
		_clientId = $"cli_{Guid.NewGuid():N}"[..20];
		await secureGate.CreateProductClientAsync(_tenantA, _clientId, Secret, "writer", "logstream");

		_logStream = new LogStreamHost(secureGate, _tenantA, _tenantB);
		await _logStream.Services.MigrateLogStreamTenantDatabasesAsync();
	}

	public async Task DisposeAsync() => await _logStream.DisposeAsync();

	private async Task<string> TokenAsync()
	{
		var response = await secureGate.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(
			new Dictionary<string, string>
			{
				["grant_type"] = "client_credentials",
				["client_id"] = _clientId,
				["client_secret"] = Secret,
				["scope"] = "logstream",
			}));
		response.EnsureSuccessStatusCode();
		return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString()!;
	}

	private async Task<HttpResponseMessage> WriteLogAsync(Guid? headerTenant)
	{
		var client = _logStream.CreateClient();
		client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync());

		if (headerTenant is { } tenant)
		{
			client.DefaultRequestHeaders.Add("X-Tenant-Id", tenant.ToString());
		}

		return await client.PostAsJsonAsync("/api/v1/log-entries", new { level = "Information", message = "compras" });
	}

	[Fact]
	public async Task ClientDeProduto_NoProprioTenant_Grava()
	{
		(await WriteLogAsync(headerTenant: null)).StatusCode.Should().Be(HttpStatusCode.Created);
	}

	[Fact]
	public async Task ClientDeProduto_HeaderDeOutroTenant_400()
	{
		(await WriteLogAsync(_tenantB)).StatusCode.Should().Be(HttpStatusCode.BadRequest,
			"tenant_id no token × X-Tenant-Id divergente é conflito no TenantResolver (ADR-0005/0037)");
	}

	[Fact]
	public async Task TokenDeClientDeProduto_NaoServeDeSubjectNaTroca()
	{
		var response = await secureGate.CreateClient().PostAsync("/connect/token", new FormUrlEncodedContent(
			new Dictionary<string, string>
			{
				["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
				["client_id"] = _clientId,
				["client_secret"] = Secret,
				["subject_token"] = await TokenAsync(),
				["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
				["scope"] = "logstream",
			}));

		response.IsSuccessStatusCode.Should().BeFalse("máquina não eleva (ADR-0031, invariante 5)");
	}
}
```

Ajustar o corpo do `POST /api/v1/log-entries` ao contrato atual do LogStream se ele exigir outros campos (conferir no `CrossProductTokenFlowTests` o corpo usado lá e copiar).

- [ ] **Step 2: Papel homônimo em outro tenant** — acrescentar à `ProductClientTokenTests` (Task 3) o teste que prova a contenção pela resolução de permissão do SecureGate (o resolvedor remoto consulta `(tenant, papel)`):

```csharp
	[Fact]
	public async Task Resolucao_PapelHomonimoEmOutroTenant_UsaSoOTenantDoToken()
	{
		var otherTenant = await IdentitySeed.TenantAsync(factory);
		await IdentitySeed.RoleAsync(factory, _tenantId, "homonimo", "log-entries:read");
		await IdentitySeed.RoleAsync(factory, otherTenant, "homonimo", "log-entries:write");

		var clientId = NewClientId();
		await factory.CreateProductClientAsync(_tenantId, clientId, Secret, "homonimo", "logstream");
		var token = await ReadTokenAsync(await RequestTokenAsync(clientId, "logstream"));
		var tokenTenant = token.GetClaim("tenant_id").Value;

		var reader = factory.CreateClient();
		reader.DefaultRequestHeaders.Authorization = new("Bearer",
			factory.CreateTokenWithScopes(Secco.SecureGate.Application.SecureGateScopes.AuthorizationRead));
		var permissions = await reader.GetFromJsonAsync<string[]>(
			$"/api/v1/authorization/tenants/{tokenTenant}/roles/homonimo/permissions", Json);

		permissions.Should().Equal("log-entries:read");
	}
```

- [ ] **Step 3: Run**

Run: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~ProductClientCrossProductTests|FullyQualifiedName~ProductClientTokenTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/SecureGate/Secco.SecureGate.Tests/Integration
git commit -m "test(securegate): client de produto contido no tenant por produto real e fora da troca (ADR-0037)"
```

---

### Task 13: Prova por mutação da bateria negativa

**Files:** nenhum arquivo muda ao final — cada mutação é aplicada, testada e revertida (`git checkout -- <arquivo>`).

Para cada linha: aplicar a mutação, rodar o filtro, confirmar **FAIL**, reverter, anotar o resultado para o registro da Task 14.

- [ ] **Step 1: Run the mutations**

| # | Invariante | Mutação | Filtro de teste que precisa falhar |
| --- | --- | --- | --- |
| 1 | `tenant_id` no token | comentar `identity.SetClaim(SeccoClaims.TenantId, ...)` em `TokenEndpoints` | `ProductClientCrossProductTests` (header divergente) e `ProductClientTokenTests` |
| 2 | papel homônimo | em `ProductClientTokenTests`, n/a — mutar o resolvedor: em `GetRolePermissionsHandler`, ignorar o `tenantId` (consultar o primeiro tenant que tiver o papel) | `Resolucao_PapelHomonimoEmOutroTenant` |
| 3 | escopo de infraestrutura no registro | `IsProductScope` → `return true;` | `ProductClientApiTests.Create_EscopoDeInfraestrutura_400` e `ProductClientHandlersTests` |
| 4 | escopo de infraestrutura na emissão | remover o `if (request.GetScopes().Any(...))` da emissão | `ClientCredentials_EscopoDeInfraestruturaGravadoNoBanco` |
| 5 | tenant desativado | trocar `is not { IsActive: true }` por `is null` | `ClientCredentials_TenantDesativado` |
| 6 | rotação mata o antigo | em `RotateSecretAsync`, não chamar `UpdateAsync` | `Rotate_SecretAntigoMorreNovoFunciona` |
| 7 | revogação | em `DeleteAsync` do store, não chamar `applications.DeleteAsync` | `Delete_RepetidoResponde404ESemToken` |
| 8 | plataforma invisível | remover `&& a.Origin == ClientOrigin.Api` de `TenantClients` e o `TenantId ==` | `ClientDePlataforma_InvisivelPelaApi`, `ClientDeOutroTenant_404EmTodasAsRotas` |
| 9 | perfil em uso por client | remover o bloco `UsedByClients` do `RoleRepository` | `DeleteRole_UsadoPorClientDoTenant_409` |
| 10 | comparação por papel inteiro | trocar o `Split(...).Contains(...)` por `list.Contains(role.Name, StringComparison.OrdinalIgnoreCase)` | `DeleteRole_ClientComPapelDeNomeParecido_NaoBloqueia` |
| 11 | reconciliação não toca a API | remover `a.Origin == ClientOrigin.Configuration &&` da consulta `stale` | `Seed_ConfigurationNaoDeclarado_EhRemovidoEApiPreservado` |
| 12 | configuração inválida derruba | remover o `else if (... StartsWith(ProductClientRules.ClientIdPrefix ...))` do validator | `Startup_ClientIdComPrefixoDaApi_Falha` e `Validate_ClientIdInvalido_Falha` |
| 13 | troca recusa token de máquina | (já protegido pela ADR-0031) remover a checagem de `subject_token_type`/sujeito usuário no `HandleElevationAsync` | `TokenDeClientDeProduto_NaoServeDeSubjectNaTroca` |

Run para cada linha: `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter "FullyQualifiedName~<filtro>"`
Expected: FAIL com a mutação; PASS depois de reverter.

Se alguma mutação **não** derrubar o teste, parar: o teste não protege a invariante. Corrigir o teste (não a mutação) e repetir a linha.

- [ ] **Step 2: Confirm clean tree** — `git status` sem alterações.

---

### Task 14: Documentação, verificação completa e fechamento

**Files:**
- Modify: `CHANGELOG.md` (seção do SecureGate)
- Modify: `docs/getting-started.md` (seção "Clients")
- Modify: `docs/roadmap.md` (#31 entregue + entrada de incremento)
- Modify: `CLAUDE.md` (linha de estado)
- Modify: `docs/design-decisions-log.md` (registro curto das decisões e da prova por mutação)

- [ ] **Step 1: CHANGELOG** — na seção do SecureGate, nova entrada:

```markdown
### Clients de produto e de plataforma (ADR-0037, issue #31)

- **Novo:** `/api/v1/tenants/{tenantId}/clients` — registrar, listar, detalhar, alterar acesso,
  rotacionar secret e revogar client `client_credentials` vinculado a tenant (`securegate:admin`).
  O token sai com `tenant_id`. `Secco.SecureGate.Client` 0.15.0 (aditivo).
- **Quebra — ação necessária antes de atualizar:** clients sem tenant passam a nascer só de
  `SecureGate:PlatformClients` na configuração do SecureGate, reconciliada pelo seed de referência.
  Todo client que você inseriu à mão no banco e **não** declarar ali é **removido** na primeira
  execução do seed. Declare cada um (`ClientId`, `Type`, `Scopes`, `Roles`, URIs; o
  `ClientSecret` por variável de ambiente, ex. `SecureGate__PlatformClients__0__ClientSecret`).
- **Quebra:** excluir perfil usado por client de máquina responde `409 SecureGate.Role.UsedByClients`.
- Fora de Development, o seed de referência ainda não roda sozinho — ver issue #34.
```

- [ ] **Step 2: getting-started** — nova seção "Clients OAuth" com: os dois tipos; exemplo de `SecureGate:PlatformClients` (o JSON da spec, com o secret por variável de ambiente); como registrar client de produto pelo `Secco.SecureGate.Client`; escopos permitidos (`logstream`, `notificationhub`); aviso de que o secret aparece uma única vez.

- [ ] **Step 3: roadmap** — na tabela de demandas, a linha da #31 passa para `**Entregue** (AAAA-MM-DD) — ADR-0037; Secco.SecureGate.Client 0.15.0, aditivo. Em produção depende da #34`. Acrescentar entrada em "Incrementos pós-Fase 8" no estilo das vizinhas (o que entrou, por quê, segurança, mutação N de N, contagem de testes).

- [ ] **Step 4: CLAUDE.md** — no parágrafo "Estado atual", depois da frase do mapeamento grupo→perfil, acrescentar uma frase: clients de produto vinculados a tenant pela API e clients de plataforma por `SecureGate:PlatformClients` reconciliados no seed de referência (ADR-0037, issue #31); atualizar a versão corrente do `Secco.SecureGate.Client` para 0.15.0 **só depois** de publicado.

- [ ] **Step 5: design-decisions-log** — seção "Clients de produto e de plataforma (issue #31, 2026-10-08)": as cinco perguntas e respostas da conversa de design, a emenda (seed × startup) e o resultado da Task 13.

- [ ] **Step 6: Full verification** (pelo comando documentado — memória do projeto)

Run:
```bash
dotnet build Secco.Platform.slnx --configuration Release
dotnet test Secco.Platform.slnx
```
Expected: build sem warning; todos os testes verdes. Anotar a contagem total para o roadmap.

- [ ] **Step 7: Commit**

```bash
git add CHANGELOG.md docs CLAUDE.md
git commit -m "docs: fecha a entrega de clients de produto e de plataforma (issue #31, ADR-0037)"
```

- [ ] **Step 8: Publicação** — **não** taguear sem confirmar com o usuário. Quando autorizado, seguir a skill `secco-platform-release` para `Secco.SecureGate.Client` 0.15.0, e depois fechar a issue #31 com um comentário que aponte a ADR-0037, a nota de upgrade e a dependência da #34.
