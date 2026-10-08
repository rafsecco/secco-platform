# Clients OAuth de produto e de plataforma — design

**Data:** 2026-10-08
**Status:** aprovada (2026-10-08)
**Decisões arquiteturais:** [ADR-0037](../../adr/secco-platform-adrs.md) (esta entrega), [ADR-0024](../../adr/secco-platform-adrs.md) (emendada), [ADR-0005](../../adr/secco-platform-adrs.md) (resolução de tenant), [ADR-0021](../../adr/secco-platform-adrs.md) (papel + permissão), [ADR-0032](../../adr/secco-platform-adrs.md) (token de máquina sem `sver`), [ADR-0034](../../adr/secco-platform-adrs.md) (idempotência), [ADR-0035](../../adr/secco-platform-adrs.md) (estado entre instâncias), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança)
**Origem:** issue [#31](https://github.com/rafsecco/secco-platform/issues/31) (`adopter-demand`, `secco-intranet`), ampliada na conversa de design para incluir o caminho de produção dos clients de plataforma.

## Contexto

Confirmado no código em 2026-10-08:

- `TokenEndpoints.HandleClientCredentialsAsync` emite sem `tenant_id`; com isso o `TenantResolver` (`src/SDK/Secco.SDK.AspNetCore/Tenancy/TenantResolver.cs`) aceita o `X-Tenant-Id` sozinho.
- Os papéis do client (`OidcApplication.Roles`, coluna `ds_roles`) são strings globais, resolvidas por `(tenant alvo, papel)` — papel homônimo no tenant alvo concede o que ele tiver lá.
- Os únicos `OpenIddictApplicationDescriptor` estão no `SecureGateDevelopmentDataSeeder`; nenhum client nasce fora de DEV.
- `catalog:<produto>` lista connection strings de todos os tenants; `authorization:read` recebe o tenant no path.
- Sem `sver`, o SDK trata o token como de máquina (`SessionVersionChecker.cs:30`) — pôr `tenant_id` no token de máquina não aciona a checagem de sessão da ADR-0032.
- `DeleteRoleHandler` já recusa perfil com membros (`HasMembers`).

## Decisões (aprovadas na conversa de design)

1. **Escopo:** a #31 **e** o caminho de produção dos clients de plataforma, na mesma entrega.
2. **Client de plataforma por reconciliação declarativa** na subida; configuração é a fonte da verdade, remove o que sai dela.
3. **Escopos de client de produto: lista fechada no código**, só APIs de produto; escopos de infraestrutura exclusivos da plataforma.
4. **Papéis do client de produto: perfis existentes do tenant**, reservados recusados.
5. **Rotação imediata**, sem sobreposição.
6. **Sem camada de compatibilidade** para clients existentes: quebra aceita, nota de upgrade (decisão do dono do produto).

## Modelo

`OidcApplication` (`src/SecureGate/Secco.SecureGate.Infrastructure/OpenIddict/`) ganha:

| Propriedade | Tipo | Coluna (convention ADR-0017) | Regra |
| --- | --- | --- | --- |
| `TenantId` | `Guid?` | `id_fk_tenant` | FK para `tb_tenants`, `Restrict` na exclusão; nulo = client de plataforma |
| `Origin` | `ClientOrigin` (`Api` \| `Configuration`) | `ie_origin` | obrigatório; `Api` ⇔ `TenantId` preenchido (check constraint) |
| `Name` | `string?` | `ds_name` | nome legível do client de produto, 1–100; único por `(TenantId, Name)` |

Migration nos dois engines (SQL Server e PostgreSQL). Linhas existentes recebem `Origin = Configuration`, `TenantId = null`.

## Emissão do token (client credentials)

Em `HandleClientCredentialsAsync`, depois da validação nativa do OpenIddict:

1. Carrega a `OidcApplication`.
2. **Se `TenantId` preenchido** (client de produto):
   - tenant inexistente ou desativado → `invalid_client`, sem detalhe;
   - algum escopo pedido fora de `SecureGateScopes.ProductScopes` → `invalid_scope` (defesa em profundidade: o registro já barra, mas o banco pode ser editado à mão);
   - token sai com `sub` = `client_id`, **`tenant_id`** = vínculo, `role` = `ds_roles`, escopos e audiences como hoje.
3. **Se `TenantId` nulo** (client de plataforma): comportamento atual, inalterado.

Recusas indistinguíveis entre si (mesma resposta para tenant desativado, client inexistente e secret errado).

## Lista de escopos

Em `SecureGateScopes` (Application):

- `ProductScopes = ["logstream", "notificationhub"]` — os únicos concedíveis a client de produto. Produto novo entra por código, no mesmo PR que registra o escopo no seed de referência.
- Infraestrutura (nunca a client de produto): `securegate:admin`, `authorization:read`, `catalog:*`, `securegate`.

## API de clients de produto

Grupo `/api/v1/tenants/{tenantId:guid}/clients`, tag `Clients`, escopo `securegate:admin`.

| Operação | Método e rota | `operationId` | Respostas |
| --- | --- | --- | --- |
| Registrar | `POST /clients` | `CreateProductClient` | `201` + `CreatedProductClientDto` (com secret); `400` validação; `404` tenant; `409` nome duplicado ou tenant desativado |
| Listar | `GET /clients` | `ListProductClients` | `200` + `IReadOnlyList<ProductClientDto>` |
| Detalhar | `GET /clients/{clientId}` | `GetProductClient` | `200`; `404` |
| Alterar acesso | `PUT /clients/{clientId}` | `UpdateProductClient` | `204`; `400`; `404`; `409` nome duplicado |
| Rotacionar | `POST /clients/{clientId}/rotate-secret` | `RotateProductClientSecret` | `200` + `ProductClientSecretDto`; `404` |
| Revogar | `DELETE /clients/{clientId}` | `DeleteProductClient` | `204`; `404` |

DTOs:

- `CreateProductClientRequest` / `UpdateProductClientRequest`: `name`, `scopes[]`, `roles[]`.
- `ProductClientDto`: `clientId`, `name`, `scopes[]`, `roles[]`, `createdAt`. **Nunca o secret.**
- `CreatedProductClientDto`: `ProductClientDto` + `clientSecret`.
- `ProductClientSecretDto`: `clientId`, `clientSecret`.

Validação (Application, `Result<T>`, ADR-0004):

- `name`: 1–100 caracteres após trim, sem caractere de controle.
- `scopes`: 1–10 itens distintos, todos em `ProductScopes`.
- `roles`: 0–20 itens distintos, cada um existente no tenant e não reservado (`RoleInputRules.IsAssignableToUsers`).
- Gerados pelo servidor: `clientId` = `cli_` + 16 caracteres base32 minúsculos de `RandomNumberGenerator`; secret = 32 bytes de `RandomNumberGenerator` em base64url.
- Busca por `clientId` **sempre** com `TenantId == tenantId da rota` e `Origin == Api`; qualquer outra coisa é `404`.

Idempotência (ADR-0034): `POST` de criação tem chave natural `(tenant, name)` → `409`; `PUT` é conjunto completo, repetir não muda estado; `DELETE` repetido → `404` sem outro efeito; `rotate-secret` é `POST` de efeito, nunca repetido pelo SDK, autenticado (sem exigência de limite de taxa — não é anônimo).

## Perfil em uso por client

`IRoleRepository.DeleteRoleAsync` passa a retornar `HasMembers` também quando algum `OidcApplication` com `TenantId == tenant` lista o papel em `ds_roles` (comparação por token separado por espaço, não por `Contains` de substring — `admin` não pode casar com `tenant-admin`). A mensagem do erro distingue "usado por usuários" de "usado por clients" para o admin saber onde mexer.

## Clients de plataforma por configuração

Seção `SecureGate:PlatformClients` (lista), lida por `PlatformClientsOptions` com `ValidateOnStart`:

```json
{
  "ClientId": "secco-adminportal",
  "Type": "AuthorizationCode",
  "ClientSecret": "<variável de ambiente>",
  "Scopes": ["openid", "profile", "email", "roles", "securegate:admin", "logstream"],
  "Roles": [],
  "RedirectUris": ["https://..."],
  "PostLogoutRedirectUris": ["https://..."]
}
```

- `ClientCredentials`: endpoint de token + grant client credentials + escopos.
- `AuthorizationCode`: endpoints de authorization/token/end session + code + refresh + PKCE obrigatório + redirect URIs + escopos. Consent implícito, como os clients first-party de hoje.

Validação fail-fast (startup cai, mensagem nomeia o índice e o campo, nunca o secret):

- `ClientId` em `^[a-z0-9]+(-[a-z0-9]+)*$`, até 100, sem prefixo `cli_`, sem repetição na lista.
- `Type` conhecido; `AuthorizationCode` exige ao menos uma redirect URI absoluta; fora de Development, só `https`.
- `ClientSecret` obrigatório; fora de Development, ≥ 32 caracteres.
- Escopos existentes no gerenciador de escopos (verificado depois do seed de referência).

Reconciliação (`PlatformClientReconciler`, roda depois do seed de referência e antes de aceitar requisições):

1. Para cada client declarado: se não existe, cria com `Origin = Configuration`; se existe com `Origin = Configuration`, atualiza permissões, papéis, URIs e — se `ValidateClientSecretAsync` falhar com o secret declarado — o secret.
2. Se o `ClientId` declarado existe com `Origin = Api` — impossível pelo prefixo, mas checado — o startup cai.
3. Todo client `Origin = Configuration` não declarado é removido (`DeleteAsync`, que leva junto autorizações e tokens do OpenIddict).
4. Concorrência entre instâncias: violação da unicidade de `client_id` na criação é tratada como "outra instância criou" — recarrega e segue para a atualização.
5. Log: quantos criados, atualizados e removidos, com os `ClientId` — nunca secrets.

## DEV, Docker e documentação

- `SecureGateDevelopmentDataSeeder` deixa de criar clients; fica com tenants, usuários, papéis e o vínculo de `dev-admin`. Os clients (`secco-dev-console`, `secco-dev-webapp`, `secco-adminportal`, `secco-adminportal-sessions` e os que os produtos usam) passam ao `appsettings.Development.json` do SecureGate, com os mesmos `ClientId` e secrets de hoje.
- `docker-compose.yml` e `.vscode/launch.json`: conferir que o SecureGate segue em Development e que nenhum produto depende de client que só o seeder criava.
- `docs/getting-started.md`: seção "Clients" — plataforma pela configuração (com exemplo), produto pela API.
- Nota de upgrade no `CHANGELOG.md` da raiz (seção do SecureGate): clients inseridos à mão são removidos na primeira subida; declarar em `SecureGate:PlatformClients` antes de atualizar.
- `docs/roadmap.md`: a #31 entra como entregue; `CLAUDE.md` ganha a linha de estado.

## Segurança (ADR-0020)

| Ameaça | Barreira |
| --- | --- |
| Client de produto trocando `X-Tenant-Id` | `tenant_id` no token + conflito no `TenantResolver` |
| Papel homônimo em outro tenant | resolução sempre no tenant vinculado |
| Credencial de produto alcançando segredo ou dado alheio | `ProductScopes` no registro e na emissão |
| `securegate:admin` emitindo identidade cross-tenant | API sem caminho para client sem tenant |
| Perfil recriado reativando client esquecido | `DeleteRole` → `409` com client usando o perfil |
| Tenant desativado com máquinas vivas | re-checagem na emissão |
| Enumeração de client | `404` uniforme; `clientId` aleatório |
| Secret em log ou resposta | só no `201` e no `rotate-secret`; persistido como hash; reconciliação nunca loga secret |
| Configuração maliciosa ou errada | validação fail-fast; quem altera configuração já tem a chave de assinatura |
| Dependência nova | nenhuma |

## Testes

Unit (Application): validação de nome, escopos, papéis; geração de `clientId`/secret; regras de `PlatformClientsOptions`.

Integração (SecureGate, Testcontainers):

- CRUD completo; repetição de `PUT` e `DELETE` (ADR-0034); `409` de nome duplicado.
- Token de client de produto carrega `tenant_id` e `role`; o de plataforma não carrega `tenant_id`.
- Reconciliação: cria, atualiza secret, remove o que saiu, não toca `Origin = Api`, configuração inválida derruba o host.

Bateria negativa provada por mutação (cada invariante quebrada de propósito precisa derrubar seu teste):

1. Token de client de produto com `X-Tenant-Id` divergente → `400` no LogStream (E2E).
2. Papel homônimo com escrita em outro tenant não concede nada.
3. `securegate:admin`, `authorization:read`, `catalog:logstream` e `securegate` recusados no registro.
4. Escopo de infraestrutura injetado direto no banco → recusado na emissão.
5. Tenant desativado → sem token.
6. Secret antigo recusado após rotação.
7. Client revogado sem token.
8. Client de plataforma → `404` em todas as rotas da API.
9. Client de outro tenant → `404`.
10. `DeleteRole` com client usando o perfil → `409`; papel `admin` em client não bloqueia exclusão de `tenant-admin`.
11. Reconciliação remove `Configuration` não declarado e preserva `Api`.
12. Token de client de produto não serve de subject na troca da ADR-0031.

Contrato: `OpenApiContractTests` com `SECCO_UPDATE_OPENAPI=true`, client regenerado no mesmo commit.

## Publicação

`Secco.SecureGate.Client` 0.15.0, aditivo (seis métodos novos). Seguir a skill `secco-platform-release`.

## Fatias sugeridas para o plano

1. Modelo + migration + emissão com `tenant_id` + lista de escopos (sem API ainda; testado com client criado no teste).
2. API de clients de produto + `DeleteRole` + contrato + client.
3. Reconciliação de clients de plataforma + saída do seeder de DEV + docs/compose.
4. Bateria negativa por mutação, nota de upgrade, roadmap, publicação.

## Fora desta entrega

- Tela no AdminPortal (a Intranet consome pelo client).
- Client público (SPA/mobile, sem secret).
- Rotação com sobreposição de secrets.
- Client vinculado a mais de um tenant.
- Catálogo filtrado por tenant para client de produto.
