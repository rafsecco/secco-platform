# Isolamento de tenant no banco do SecureGate, rodada 1 — design

**Data:** 2026-10-17
**Status:** em revisão
**Decisões arquiteturais:** [ADR-0041](../../adr/secco-platform-adrs.md) (esta entrega), [ADR-0040](../../adr/secco-platform-adrs.md) (pessoa em vários tenants), [ADR-0021](../../adr/secco-platform-adrs.md) (perfis por tenant), [ADR-0002](../../adr/secco-platform-adrs.md) (camadas), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança)
**Origem:** issue [#32](https://github.com/rafsecco/secco-platform/issues/32). Rodada 1 = camadas 1 (filtro global no EF) e 3 (teste de arquitetura). A camada 2 (RLS) é a rodada 2, com spec própria.

## Contexto

Confirmado no código em 2026-10-17:

- Nenhum `HasQueryFilter` no `SecureGateDbContext`; filtros manuais (`TenantId == …`) em `Users/UserAccountService.cs` (19), `Roles/RoleRepository.cs` (8), `Tenants/TenantMembershipRepository.cs` (5), `Tenants/TenantRepository.cs` (4), `Federation/GroupRoleMappingRepository.cs` (3), `Clients/OpenIddictProductClientStore.cs` (1) e nos seeders.
- Entidades com tenant: `Role.TenantId`, `TenantMembership`, `TenantDatabase`, `TenantFederation`, `TenantGroupRoleMapping`, `ElevationGrant`, `OidcApplication.TenantId`; `User.TenantId` é o tenant **dono** (ADR-0040).
- O Identity (`UserManager`, `SignInManager`, `UserStore`) consulta `tb_users`, `tb_user_roles`, `tb_roles` etc. pelo mesmo `SecureGateDbContext`: os filtros valem para ele também.
- Grupos de rota com tenant: `/api/v1/tenants/{tenantId:guid}/{users|roles|members|clients|entra}`, `/api/v1/tenants/{tenantId:guid}/users/{userId:guid}/elevation`, `/api/v1/authorization/tenants/{tenantId:guid}/roles/{role}/permissions`; o grupo `/api/v1/tenants` usa `{id:guid}` nas rotas de tenant (detalhe, ativar/desativar, bancos, provisionamento, federação). Sem tenant: `/api/v1/catalog/{product}`, `/api/v1/permission-catalog`, `/api/v1/authorization/users/{sub}/session-version`, `/connect/*`, páginas Razor (`/login`, `/login/dois-fatores`, `/conta/*`).
- EF Core 10 tem filtros nomeados (`HasQueryFilter("nome", …)`).

## Decisões (ADR-0041, aprovadas na conversa de 2026-10-15/17)

1. Escopo explícito com três estados (nenhum, tenant, plataforma); o `IgnoreQueryFilters` é proibido.
2. Fail-closed: sem escopo, as entidades de tenant não aparecem.
3. Lista de entidades filtradas e fora do filtro como na seção "Filtros".
4. Checagem de escrita no `SaveChanges`.
5. Allowlist versionada das entradas em modo plataforma, imposta por teste de arquitetura.
6. Filtros manuais existentes permanecem (redundância inofensiva).

## Escopo de acesso

**Porta** `src/SecureGate/Secco.SecureGate.Application/Tenancy/ITenantAccessScope.cs`:

```csharp
public enum TenantAccessMode { None, Tenant, Platform }

public interface ITenantAccessScope
{
    TenantAccessMode Mode { get; }
    Guid? TenantId { get; }                       // só em Mode == Tenant
    IDisposable EnterTenant(Guid tenantId);       // Guid.Empty → ArgumentException
    IDisposable EnterPlatform(string reason);     // motivo obrigatório, não vazio
}
```

- Implementação `TenantAccessScope` na Infrastructure, **scoped**, com pilha de estados: `Dispose` restaura exatamente o estado anterior (aninhar plataforma → tenant → volta a plataforma).
- `EnterPlatform` loga o motivo em `Debug` (`[LoggerMessage]`), nunca dados da requisição.
- O `SecureGateDbContext` recebe o `ITenantAccessScope` no construtor e expõe-o aos filtros por propriedades da instância do contexto (o padrão do EF para filtro parametrizado por contexto).
- Contextos criados fora de DI (design-time das migrations) usam um escopo nulo em modo plataforma — `IDesignTimeDbContextFactory` existente, se houver; nunca em runtime.

## Filtros

Filtro nomeado `"Tenant"` em cada entidade abaixo. `P` = modo plataforma; `T` = tenant do escopo.

| Entidade | Predicado |
| --- | --- |
| `Role` | `P \|\| r.TenantId == T` |
| `RoleClaim` | `P \|\| Roles.Any(r => r.Id == rc.RoleId && r.TenantId == T)` |
| `UserRole` | `P \|\| Roles.Any(r => r.Id == ur.RoleId && r.TenantId == T)` |
| `TenantMembership` | `P \|\| m.TenantId == T` |
| `User` | `P \|\| u.TenantId == T \|\| Memberships.Any(m => m.UserId == u.Id && m.TenantId == T)` |
| `TenantDatabase`, `TenantFederation`, `TenantGroupRoleMapping` | `P \|\| x.TenantId == T` |
| `ElevationGrant` | `P \|\| g.TenantId == T` |

Com `Mode == None`, `P` é falso e `T` é nulo: nenhum predicado casa (fail-closed).

**Fora do filtro** (comentário no mapeamento explicando cada uma): `Tenant`; `PermissionCatalogEntry`; `DataProtectionKey`; `UserLogin`, `UserClaim`, `UserToken`; tabelas do OpenIddict (`OidcApplication`, `OidcAuthorization`, `OidcScope`, `OidcToken`).

O filtro de `User` com `Memberships.Any` usa o `tb_tenant_memberships` sem filtro dele mesmo dentro do predicado (o EF não aplica filtro de outra entidade dentro de um predicado escrito com `Set<>()` — conferir no teste; se aplicar, o resultado é o mesmo, já que o filtro do vínculo também é `TenantId == T`).

## Escrita

Override de `SaveChanges`/`SaveChangesAsync` no `SecureGateDbContext`: em `Mode == Tenant`, toda entrada `Added`/`Modified` das entidades com `TenantId` direto (`Role`, `TenantMembership`, `TenantDatabase`, `TenantFederation`, `TenantGroupRoleMapping`, `ElevationGrant`) precisa ter `TenantId == T`; senão `InvalidOperationException` com o tipo da entidade (sem ids). `UserRole`/`RoleClaim` não carregam tenant: a garantia vem de o perfil referenciado só ser carregável no escopo. `User` não entra na checagem: a pessoa é criada pelo `UserManager` com o tenant dono, e alterada por operações de pessoa só pelo tenant dono (ADR-0040). Em `Mode == None`, escrever entidade com tenant também lança. Em `Mode == Platform`, nada é checado.

`ExecuteUpdate`/`ExecuteDelete` (usados em `TenantMembershipRepository.RemoveAsync`) passam pelo filtro de consulta do EF — a parte `Where` já é restrita pelo escopo; teste fixa isso.

## Borda HTTP

Endpoint filter `TenantScopeEndpointFilter` (Api) aplicado:

- aos grupos `/api/v1/tenants/{tenantId:guid}/...` (users, roles, members, clients, entra, elevation), lendo `tenantId`;
- às rotas de tenant do grupo `/api/v1/tenants` com `{id:guid}`, lendo `id`;
- a `/api/v1/authorization/tenants/{tenantId:guid}/roles/{role}/permissions`.

Lê o valor da rota (já validado como Guid pelo roteamento), entra no escopo do tenant e o mantém até o fim do handler. Quem prova que o tenant existe segue sendo o handler.

Rotas sem tenant que entram em modo plataforma (um ponto cada, na allowlist):

| Ponto | Motivo |
| --- | --- |
| `GET/POST /api/v1/tenants` (listar/criar) | operação de instalação |
| `/api/v1/catalog/{product}` | catálogo de bancos de todos os tenants (`catalog:*`) |
| `/api/v1/permission-catalog` | dado de plataforma; perfis tocados só pela limpeza por tenant, que está sob rota com tenant |
| `/api/v1/authorization/users/{sub}/session-version` | estado da pessoa, em qualquer tenant |
| `/connect/token`, `/connect/authorize`, `/connect/userinfo`, `/connect/logout` | protocolo: a pessoa é achada em modo plataforma; perfis lidos em `EnterTenant(loginTenantId)` |
| Filtro de páginas Razor (`IAsyncPageFilter`) para `/login`, `/login/dois-fatores`, `/conta/*` | a pessoa autenticada (ou o e-mail do formulário) não tem tenant de operação |
| Login federado (`FederatedLoginEndpoints`, `EntraSignInProcessor`) | casa a pessoa por e-mail/`oid` dentro do `tid` registrado |
| `UserAccountService.FindPersonByEmailAsync` | inclusão de membro: a pessoa é de outro tenant dono |
| `OperatorGuard` / `CountActiveOperatorsAsync` | conta operadores no tenant de plataforma — trocar para `EnterTenant(SecureGatePlatform.TenantId)` em vez de plataforma, se couber |
| Seeders (referência, desenvolvimento, recifragem), reconciliador de clients de plataforma, `migrate`, verificação de migration na subida | fora de requisição |
| `HandleElevationAsync` (token exchange) | a pessoa é achada em modo plataforma; a concessão é lida em `EnterTenant(user.TenantId)` |

Preferência registrada: **entrar no tenant sempre que o tenant for conhecido**; plataforma só onde ele não existe. Cada linha acima que puder virar `EnterTenant` vira.

## Teste de arquitetura

`tests/SecureGate/Secco.SecureGate.Tests/Architecture/TenantIsolationArchitectureTests.cs`, lendo o código-fonte de `src/SecureGate` (texto, sem Roslyn):

1. Nenhum `IgnoreQueryFilters` em arquivo `.cs` fora de `Migrations/`.
2. Cada ocorrência de `EnterPlatform(` está em `tests/SecureGate/Secco.SecureGate.Tests/Architecture/platform-scope-allowlist.txt`, formato `caminho/relativo.cs | motivo` — uma linha por ocorrência (duas chamadas no mesmo arquivo = duas linhas). Ocorrência sem linha reprova; linha sem ocorrência também reprova (allowlist não acumula entulho).

## Testes

**Unitários:** `TenantAccessScope` (estados, pilha, `Dispose` restaura, argumentos inválidos).

**Integração** (`Integration/TenantIsolationTests.cs`, dados em dois tenants A e B, mais uma pessoa dona em A e membro em B):

1. Para cada entidade filtrada: escopo A não vê nada de B; escopo nenhum não vê nada; plataforma vê tudo.
2. Pessoa: aparece em A (dona) e em B (membro); some de B quando o vínculo é removido; não aparece em C.
3. `RoleClaim`/`UserRole` de B invisíveis em escopo A, inclusive pelo `UserManager.GetRolesAsync`.
4. Escrita: incluir `Role`/`TenantMembership`/`TenantDatabase` de B em escopo A lança; em escopo nenhum lança; em plataforma passa.
5. `ExecuteDelete` em escopo A não apaga linha de B.
6. Borda: uma rota de cada grupo com tenant lê só o tenant da rota (ex.: `GET /tenants/A/roles` não lista perfil de B mesmo com filtro manual removido — ver mutação).
7. Arquitetura: os dois testes acima, mais prova por mutação de que reprovam (`IgnoreQueryFilters` inserido; `EnterPlatform` novo sem linha; linha órfã).

**Regressão:** suíte inteira do SecureGate verde sem afrouxar asserção. Teste existente quebrado = caminho cross-tenant faltando na tabela da borda; entra na allowlist **com motivo** e é relatado na revisão.

**Mutação (revisor):** remover o filtro de cada entidade com o filtro manual correspondente também removido (para provar que o filtro global, sozinho, segura); trocar fail-closed por "sem filtro"; remover a checagem de escrita; remover o `TenantScopeEndpointFilter` de um grupo.

## Segurança (ADR-0020)

- O tenant do escopo vem da rota (validada pelo roteamento) ou de valor emitido pelo próprio SecureGate (token/cookie validados) — nunca de corpo ou header.
- Fail-closed por padrão: esquecer de entrar no escopo vira lista vazia ou `404`, nunca vazamento.
- Modo plataforma: superfície listada e imposta por teste; o motivo não vai a resposta nem a log de nível acima de `Debug`.
- Sem dependência nova; sem mudança de contrato.

## Fora desta rodada

RLS e separação de credenciais (rodada 2); remoção dos filtros manuais redundantes; exportar/excluir dados de um tenant; filtro nas tabelas do OpenIddict.
