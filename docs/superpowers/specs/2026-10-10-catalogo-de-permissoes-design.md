# Catálogo de permissões publicado por produto — design

**Data:** 2026-10-10
**Status:** aguardando revisão
**Decisões arquiteturais:** [ADR-0039](../../adr/secco-platform-adrs.md) (esta entrega), [ADR-0021](../../adr/secco-platform-adrs.md) (papel + permissão), [ADR-0038](../../adr/secco-platform-adrs.md) (`migrate` e seed de referência), [ADR-0037](../../adr/secco-platform-adrs.md) (escopos de infraestrutura), [ADR-0034](../../adr/secco-platform-adrs.md) (idempotência), [ADR-0003](../../adr/secco-platform-adrs.md) (admissão no SharedKernel), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança)
**Origem:** issue [#33](https://github.com/rafsecco/secco-platform/issues/33) (`adopter-demand`, `secco-intranet`).

## Contexto

Confirmado no código em 2026-10-10:

- `SetRolePermissionsHandler` (`src/SecureGate/Secco.SecureGate.Application/Roles/SetRolePermissionsHandler.cs`) só valida o formato (`SeccoPermissions.IsValid`) e o limite de 200 permissões por perfil.
- Constantes por produto: `src/LogStream/Secco.LogStream.Application/LogStreamPermissions.cs`, `src/NotificationHub/Secco.NotificationHub.Application/NotificationHubPermissions.cs`, `templates/secco-service/Secco.SampleService.Application/SampleServicePermissions.cs`. As descrições já existem nos comentários XML de cada constante.
- `RoleDetailDto(string Name, IReadOnlyList<string> Permissions, bool IsReserved, int MemberCount)`; rota `GET /api/v1/tenants/{tenantId}/roles/{role}` (`GetRole`) e `PUT .../roles/{role}/permissions` (`SetRolePermissions`).
- `Secco.SecureGate.Client` usa a seção `Secco:SecureGate` (`SecureGateClientCredentialsOptions`: `BaseUrl`, `ClientId`, `ClientSecret`, `Product`) e o `SeccoClientCredentialsHandler` com um escopo por recurso (`catalog:<produto>`, `authorization:read`).
- `IReferenceDataSeeder` vive no `Secco.SDK.EntityFrameworkCore`; o `Secco.SecureGate.Client` não referencia esse pacote.

## Decisões (aprovadas na conversa de design)

1. Publicação no `migrate`, pelo produto, com escopo `permissions:<produto>`.
2. Catálogo por produto, não por tenant.
3. `SetRolePermissions` recusa (`400`) permissão **acrescentada** que não esteja no catálogo de nenhum produto.
4. Órfã fica visível (`unknownPermissions`) e só sai por ação do admin; publicar nunca toca perfil.
5. (Ajuste na escrita) O client entrega só o publicador; o SDK EF Core entrega um seeder por delegate; o produto liga os dois — o client não arrasta o EF Core.

## SharedKernel

`src/SharedKernel/Secco.SharedKernel/Authorization/SeccoPermissionDefinition.cs`:

```csharp
/// Uma permissão declarada por um produto, com descrição para quem administra perfis (ADR-0039).
public sealed record SeccoPermissionDefinition(string Name, string Description);
```

Sem validação embutida (o kernel não tem I/O nem regra de negócio); quem valida é o SecureGate na publicação.

## SecureGate

**Domínio/persistência:** entidade `PermissionCatalogEntry` (Domain): `Id`, `Product` (máx. 50, kebab-case, mesma regra de `TenantDatabase.ProductMaxLength`), `Name` (máx. o limite do `SeccoPermissions`), `Description` (máx. 200), `PublishedAt`. Tabela `tb_permission_catalog_entries` por convention (ADR-0017), índice único `(Product, Name)`. Migration nos dois engines. Dado de plataforma, sem tenant.

**Escopo:** `SecureGateScopes.PermissionsPrefix = "permissions:"`, `PermissionsFor(product)`. Registrado no seed de referência para cada produto que publica (`logstream`, `notificationhub`), como os `catalog:*`. Entra nos escopos de infraestrutura: nunca concedido a client de produto (`IsProductScope` continua só `logstream`/`notificationhub`; a recusa no registro e na emissão da ADR-0037 já cobre por não estar na lista).

**Endpoints** (`PermissionCatalogEndpoints`, tag `PermissionCatalog`):

| Operação | Rota | Autorização | `operationId` | Respostas |
| --- | --- | --- | --- | --- |
| Publicar | `PUT /api/v1/permission-catalog/{product}` | escopo `permissions:{product}` (o da rota) | `PublishPermissionCatalog` | `204`; `400` validação; `403` escopo de outro produto |
| Listar todos | `GET /api/v1/permission-catalog` | `securegate:admin` | `ListPermissionCatalog` | `200` + `IReadOnlyList<ProductPermissionCatalogDto>` |
| Ler um | `GET /api/v1/permission-catalog/{product}` | `securegate:admin` | `GetPermissionCatalog` | `200` + `ProductPermissionCatalogDto`; `404` |

- Corpo do `PUT`: `{ "permissions": [ { "name", "description" } ] }`.
- `ProductPermissionCatalogDto(string Product, IReadOnlyList<PermissionDefinitionDto> Permissions, DateTimeOffset PublishedAt)`; `PermissionDefinitionDto(string Name, string Description)`.
- Validação (Application, `Result`): produto kebab-case até 50; 0–200 entradas (lista vazia é válida: produto sem permissões); nome em `SeccoPermissions.IsValid`; descrição 1–200 após trim, sem caractere de controle; sem nome repetido.
- `PUT` substitui o conjunto do produto numa transação; republicar igual não altera `PublishedAt` (efeito idêntico, ADR-0034).
- A checagem do escopo contra a rota segue o filtro `ScopeAuthorization.RequireCatalogScopeAsync` existente (criar `RequirePermissionsScopeAsync` no mesmo arquivo, mesma forma).

**Perfis:**
- `SetRolePermissionsHandler` passa a: carregar as permissões atuais do perfil; calcular `acrescentadas = novas − atuais`; para as acrescentadas, consultar o catálogo (`IPermissionCatalogRepository.FindUnknownAsync(IEnumerable<string>)`) e, se houver desconhecidas, devolver `SecureGateErrors.Roles.PermissionsNotInCatalog` (`400`) com as desconhecidas no detalhe (ordenadas, no máximo 20 listadas). Perfil inexistente segue `404` como hoje.
- `RoleDetailDto` ganha `IReadOnlyList<string> UnknownPermissions` (aditivo): as permissões gravadas que nenhum catálogo declara.
- Os perfis reservados seguem o caminho atual (não editáveis).

**Client:** `Secco.SecureGate.Client` 0.16.0 — os três métodos gerados, e:

```csharp
public interface ISecureGatePermissionCatalogPublisher
{
    /// Publica (PUT) o catálogo do produto. Lança em falha (o migrate deve falhar).
    /// Sem a seção Secco:SecureGate configurada, loga e retorna sem publicar.
    Task PublishAsync(string product, IReadOnlyList<SeccoPermissionDefinition> permissions, CancellationToken cancellationToken = default);
}

public static IServiceCollection AddSecureGatePermissionCatalogPublisher(this IServiceCollection services);
```

O publicador usa um `HttpClient` nomeado próprio com `SeccoClientCredentialsHandler` pedindo `permissions:<produto>` (token store próprio, como o catálogo), e o client NSwag gerado para o `PUT`.

## SDK EF Core

`SeccoSeedingServiceCollectionExtensions.AddSeccoReferenceSeeder(this IServiceCollection services, Func<IServiceProvider, CancellationToken, Task> seed, int order = 0)` — registra um `IReferenceDataSeeder` que executa o delegate num escopo do container. Minor no `Secco.SDK.EntityFrameworkCore`.

## Produtos

LogStream e NotificationHub:
- Application: `public static IReadOnlyList<SeccoPermissionDefinition> Catalog` em `*Permissions`, com todas as constantes e as descrições dos comentários XML.
- Infrastructure (composição):

```csharp
services.AddSecureGatePermissionCatalogPublisher();
services.AddSeccoReferenceSeeder((serviceProvider, cancellationToken) =>
    serviceProvider.GetRequiredService<ISecureGatePermissionCatalogPublisher>()
        .PublishAsync("logstream", LogStreamPermissions.Catalog, cancellationToken));
```

- Teste unitário por produto: toda constante pública de `*Permissions` está em `Catalog` (reflexão) — impede esquecer de catalogar permissão nova.

Template: o mesmo para `SampleServicePermissions` (produto `sample-service`, ou o nome que o template já usa como produto).

DEV: `SecureGate:PlatformClients` do `appsettings.Development.json` do SecureGate — `secco-dev-console` ganha `permissions:logstream` e `permissions:notificationhub`.

## Segurança (ADR-0020)

| Ameaça | Barreira |
| --- | --- |
| Produto A sobrescrevendo catálogo de B | escopo `permissions:<produto>` conferido contra a rota |
| Credencial de outro time publicando | `permissions:*` fora dos escopos de produto (ADR-0037) |
| Publicação apagando acesso | publicar nunca toca perfil |
| Entrada grande/malformada | limites e formato acima; recusa antes de persistir |
| Log forging pela descrição | caractere de controle recusado |
| Leitura do catálogo | `securegate:admin` |
| Dependência nova | nenhuma |

## Testes

- Unit (SecureGate Application): validação da publicação; `SetRolePermissions` recusa acrescentada desconhecida, aceita órfã mantida, aceita acrescentada conhecida em qualquer produto.
- Integração (SecureGate): publicar/ler/republicar sem mudança; escopo de outro produto → `403`; sem escopo → `403`; `400` com desconhecidas; `unknownPermissions` após republicar sem uma permissão; republicar nunca altera perfil.
- Integração (LogStream): seeder publica no SecureGate real (padrão dos testes entre produtos); sem `Secco:SecureGate`, pula sem erro.
- Unit (LogStream, NotificationHub, template): reflexão — toda constante está no `Catalog`.
- Contrato: `openapi.json` + client.
- Mutação: escopo vs rota; recusa só do acrescentado; publicar não toca perfil; `permissions:*` fora dos escopos de produto.

## Publicação

`Secco.SharedKernel` 0.6.0, `Secco.SDK.EntityFrameworkCore` 0.6.0, `Secco.SecureGate.Client` 0.16.0, `Secco.Templates` 0.4.0, mais a cadeia do script.

## Fora desta entrega

Tela de catálogo no AdminPortal; remoção em massa de órfãs; namespace de produto no nome da permissão.
