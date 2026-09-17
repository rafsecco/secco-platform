# Sessões e revogação efetiva — design

**Data:** 2026-09-17
**Status:** aguardando revisão
**Decisão arquitetural:** [ADR-0032](../../adr/secco-platform-adrs.md) — versão de sessão e revogação efetiva
**Origem:** entrega A da decomposição da gestão de identidade (após a issue #26). As entregas B (credenciais por
e-mail) e C (conta) dependem desta: toda troca de senha, redefinição e troca de e-mail revoga sessões pela
operação definida aqui.

## Contexto

Três lacunas verificadas no código tornam "encerrar sessões" ineficaz hoje:

1. O access token JWT (60 min) é validado localmente pelos produtos e segue válido depois de qualquer revogação.
2. Nenhuma operação revoga os tokens do OpenIddict; eventos que não mudam o estado da conta (senha, e-mail) não
   fazem a renovação falhar.
3. O `/connect/authorize` aceita o cookie de login (1 h) e só verifica se o usuário existe
   (`InteractiveEndpoints.AuthorizeAsync`): o SecureGate usa `AddIdentityCore` + cookie manual, sem
   `SecurityStampValidator`. Quem tem o cookie obtém tokens novos depois de qualquer revogação.

Um quarto achado condiciona a entrega: o AdminPortal guarda **só o access token** como claim no cookie
(`AdminPortalAuthenticationExtensions`, `OnTokenValidated`) e não renova. Com access token de 5 minutos, o
operador cairia a cada 5 minutos.

## Decisões (aprovadas na conversa de design)

1. **Versão de sessão `sver`** em todo token de usuário (login, renovação, elevação), derivada do
   `SecurityStamp` por hash truncado; ausente em token de máquina.
2. **Verificação nos produtos** pelo SDK, com cache por `sub` e fail-closed, registrada junto do resolvedor de
   permissões.
3. **Revogação única**: troca do stamp, depois `RevokeBySubjectAsync` em autorizações e tokens.
4. **Cookie validado** no `/connect/authorize`: stamp contra o banco + `AccountStateGuard`.
5. **Access token padrão de 5 minutos** + **renovação no AdminPortal** com tokens fora do cookie.

## SecureGate

### Claim `sver`

- Valor: `Base64Url(SHA-256(SecurityStamp))[0..16]`. Função única `SessionVersion.From(string securityStamp)`.
- Emitida em `OidcPrincipalBuilder.ForUser` e `ForElevation`, com destino **access token** (e id_token no login,
  para o AdminPortal não precisar dela — só access token é obrigatório).
- Nunca em `HandleClientCredentialsAsync`.

### Endpoint de versão

`GET /api/v1/authorization/users/{sub}/session-version` — scope `authorization:read`.

| Situação | Resposta |
| --- | --- |
| Usuário ativo, tenant ativo | 200 `{ "sessionVersion": "<sver atual>", "revoked": false }` |
| Desativado, bloqueado, tenant inativo | 200 `{ "sessionVersion": null, "revoked": true }` |
| `sub` inexistente ou não-Guid | 200 `{ "sessionVersion": null, "revoked": true }` |

Inexistente responde como revogado de propósito: o produto recusa do mesmo jeito, e a resposta não distingue
"não existe" de "desativado" (sem enumeração, ADR-0020).

### Revogação

- `ISessionRevoker.RevokeAllAsync(User user, SessionRevocationReason reason, CancellationToken)` na
  Infrastructure: `UserManager.UpdateSecurityStampAsync` → `IOpenIddictAuthorizationManager.RevokeBySubjectAsync`
  → `IOpenIddictTokenManager.RevokeBySubjectAsync`.
- Log estruturado com `EventId` próprio: usuário, tenant, motivo. Sem e-mail, sem token.
- Gatilhos nesta entrega:

| Evento | Revoga |
| --- | --- |
| `POST /api/v1/tenants/{tenantId}/users/{userId}/sessions/revoke` (`RevokeUserSessions`) | Sempre. 204; 404 para usuário inexistente ou de outro tenant; permitido ao próprio usuário |
| `DeactivateUser` | Após desativar |
| `RemoveUserRole` | Só quando uma associação foi de fato removida |
| Tenant desativado | Não revoga um a um — o endpoint de versão responde revogado |

### Cookie no `/connect/authorize`

Depois de autenticar o cookie: `SignInManager.ValidateSecurityStampAsync(principal)` e
`AccountStateGuard.CanReceiveTokensAsync`. Falha → `SignOutAsync(IdentityConstants.ApplicationScheme)` e
challenge para o login, voltando à mesma requisição de autorização.

### Tempo de vida

Padrão de `SecureGate:Tokens:AccessTokenLifetimeMinutes` de 60 para **5** (configurável). Refresh token
inalterado.

## SDK e client

| Pacote | Mudança | Versão |
| --- | --- | --- |
| `Secco.SharedKernel` | `SeccoClaims.SessionVersion = "sver"` | 0.4.0 |
| `Secco.SDK.AspNetCore` | `ISessionVersionResolver`; `CachedSessionVersionResolver` (cache por `sub`, TTL `Secco:Authentication:SessionVersionCacheTtlSeconds`, padrão 60, fail-closed); verificação em `JwtBearerEvents.OnTokenValidated` dentro de `AddSeccoAuthentication()` | 0.8.0 |
| `Secco.SecureGate.Client` | `SecureGateSessionVersionResolver` (remoto, client credentials com `authorization:read`), registrado dentro de `AddSecureGatePermissionResolver()` | 0.8.0 |

Regras da verificação:

- Sem `sver` no token → passa.
- Sem `ISessionVersionResolver` registrado → não verifica (DEV standalone).
- `sver` diferente, ou revogado → `context.Fail(...)` → 401.
- Falha ao consultar e cache vencido → 401 (fail-closed), com log de aviso sem o token.
- Cache guarda também o estado revogado, com o mesmo TTL.

## AdminPortal

- Novo `IOperatorSessionStore` sobre `IDistributedCache` (padrão em memória): chave = id de sessão aleatório
  (256 bits, Base64Url), valor = access token, refresh token, expiração. Expiração do item = do cookie.
- `OnTokenValidated` grava no store e adiciona ao cookie **só** a claim com o id de sessão (remove a claim do
  access token).
- `OperatorTokenProvider`: lê do store; se vence em < 60 s, renova no token endpoint com o refresh token
  (client confidencial) sob `SemaphoreSlim` por sessão, e grava o novo par (o refresh é rotativo).
- Renovação recusada (`invalid_grant`) → remove a sessão do store e sinaliza reautenticação: o
  `ISecureGateClientFactory` lança uma exceção própria que as páginas convertem em navegação para o login
  (`forceLoad`).
- Sessão ausente no store (reinício do AdminPortal) → mesmo caminho de reautenticação.

## Testes

**SDK** (unitários): versão igual passa; diferente → 401; revogado → 401; sem `sver` passa; resolvedor lança e
cache vencido → 401; duas requisições no TTL → uma consulta.

**SecureGate** (integração, tokens reais):

- `sver` presente em login, renovação e elevação; ausente em client credentials.
- Endpoint de versão: ativo, desativado, bloqueado, tenant inativo, `sub` inexistente; 401 sem token; 403 sem
  `authorization:read`.
- `RevokeUserSessions`: 204; refresh recusado logo depois; 404 cross-tenant; 401/403.
- Após revogar, o mesmo navegador (cookie) no `/connect/authorize` volta para a tela de login e não recebe code.
- `DeactivateUser` revoga (refresh recusado imediatamente, sem depender do lockout); `RemoveUserRole` efetivo
  revoga; repetido não troca o stamp.
- Token de acesso padrão sai com expiração de 5 minutos.

**Ponta a ponta entre produtos:** token real aceito pelo LogStream federado → revogar → com TTL de cache de 1 s
no teste, a requisição seguinte ao LogStream responde 401.

**AdminPortal:** renovação perto do vencimento; chamadas simultâneas geram uma renovação; `invalid_grant` leva a
reautenticação; o cookie emitido não contém access token.

**Mutação:** ordem stamp → revoke; validação do stamp no authorize; fail-closed do SDK; `sver` no token de
elevação; revogado para tenant inativo; revogação só em remoção efetiva de perfil.

## Publicação

Cadeia `sharedkernel/v0.4.0` → `sdk/v0.8.0` → `sdk-credentials` (patch da cadeia) → `securegate-client/v0.8.0`,
uma tag por vez, depois do CI verde. CHANGELOG com a mudança de comportamento do access token de 5 minutos e a
instrução para clientes que não renovam token. Adoção pelo LogStream e NotificationHub no mesmo monorepo;
`secco-intranet` avisado para atualizar os pacotes.

## Fora desta entrega

- Auditoria no LogStream (entrega B).
- Eventos de senha e troca de e-mail como gatilhos (entregas B e C, reusando `ISessionRevoker`).
- Cache distribuído para mais de uma instância do AdminPortal.
- Revogação de client de máquina (rotação de secret).
