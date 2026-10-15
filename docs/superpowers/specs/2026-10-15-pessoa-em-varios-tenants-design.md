# Pessoa membro de vários tenants — design

**Data:** 2026-10-15
**Status:** aprovada (2026-10-15)
**Decisões arquiteturais:** [ADR-0040](../../adr/secco-platform-adrs.md) (esta entrega), [ADR-0022](../../adr/secco-platform-adrs.md) (identidade, emendada), [ADR-0037](../../adr/secco-platform-adrs.md) (clients, emendada), [ADR-0021](../../adr/secco-platform-adrs.md) (perfis por tenant), [ADR-0023/0024](../../adr/secco-platform-adrs.md) (operador sem `tenant_id`), [ADR-0026](../../adr/secco-platform-adrs.md) (federação por tenant), [ADR-0031](../../adr/secco-platform-adrs.md) (elevação), [ADR-0032](../../adr/secco-platform-adrs.md) (revogação), [ADR-0034](../../adr/secco-platform-adrs.md) (idempotência), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança)
**Origem:** issue [#35](https://github.com/rafsecco/secco-platform/issues/35). Spike na branch `spike/35-varios-tenants` (descartável; serve de referência, não de base).

## Contexto

Confirmado no código em 2026-10-15:

- `User.TenantId` (`Infrastructure/Identity/IdentityEntities.cs`) é o único vínculo pessoa ↔ tenant. Perfis (`Role.TenantId`) já são por tenant.
- O login lê **todos** os perfis da conta: `userManager.GetRolesAsync(user)` em `InteractiveEndpoints.AuthorizeAsync` (linha 88), `TokenEndpoints.HandleUserGrantAsync` (linha 151) e `Pages/Account/TwoFactor.cshtml.cs` (linhas 146 e 186).
- `OidcPrincipalBuilder.ForUser` grava `tenant_id = user.TenantId`; `InstallationOperatorPolicy.IsInstallationOperator` só olha `user.TenantId` e o nome do perfil.
- `AccountStateGuard.CanReceiveTokensAsync` confere o tenant de `user.TenantId`.
- `UserAccountService` (implementação de `IUserDirectory`) filtra tudo por `user.TenantId == tenantId`. Isso já é "tenant dono" nas operações de pessoa (convite, reset de senha, login local, nome de exibição, reset do segundo fator, login externo, ativar/desativar, encerrar sessões, elevação). **Defeito existente:** `ListByTenantAsync` junta os perfis da pessoa **sem** filtrar `role.TenantId` (linhas 268–274) — hoje não aparece porque a pessoa só tem perfis do próprio tenant.
- `OidcApplication.TenantId` + `Origin` (ADR-0037), com a restrição `ck_oidc_applications_origin_tenant` (`Origin = Api` ⇔ tenant preenchido). A API de clients do tenant (`ProductClientEndpoints`, `ProductClientCommand(Name, Scopes, Roles)`) só cria `client_credentials`.
- `ISessionRevoker.RevokeAllAsync(userId, SessionRevocationReason, ...)` move o `SecurityStamp` e revoga autorizações e tokens do OpenIddict.

## Decisões (ADR-0040, aprovadas em 2026-10-15)

1. Vínculo N:N em tabela própria; e-mail continua único na instalação.
2. `User.TenantId` vira **tenant dono**: tem sempre vínculo ativo, governa a credencial, a federação e a auditoria da elevação. Desativado, bloqueia a pessoa em todo lugar.
3. Tenant do login = tenant do client interativo; client sem vínculo (plataforma) = tenant dono.
4. Perfis no token: só os do tenant do login. Operador: tenant do login = plataforma **e** dono = plataforma.
5. Operações de pessoa só pelo tenant dono (`404` pelos outros); operações de vínculo por qualquer tenant de que a pessoa é membro.
6. Suspender ou remover vínculo revoga as sessões da pessoa (`SecurityStamp`).
7. Client interativo vinculado a tenant pela API do tenant.

## Modelo de dados

**Entidade `TenantMembership`** (Domain, `Domain/Tenants/TenantMembership.cs`, no padrão de `TenantGroupRoleMapping`): `UserId`, `TenantId`, `IsActive`, `CreatedAt`. Construtor valida ids não vazios; métodos `Suspend()` e `Reactivate()`.

- Tabela `tb_tenant_memberships` por convention (ADR-0017); PK composta `(UserId, TenantId)` → `id_pfk_user`, `id_pfk_tenant`; `fl_active`, `dt_created_at`.
- FKs para `tb_users` (cascade: apagar a pessoa apaga os vínculos) e `tb_tenants` (restrict).
- Índice por `TenantId` (listagem do tenant).
- **Migration nos dois engines**, com o dado: um vínculo ativo por usuário existente, no `TenantId` dele (`INSERT ... SELECT` na própria migration, `dt_created_at` = agora).

**Repositório** `ITenantMembershipRepository` (Application, porta; Infrastructure, implementação):

| Método | Uso |
| --- | --- |
| `GetAsync(tenantId, userId)` | estado do vínculo (`null` = não membro) |
| `IsActiveMemberAsync(tenantId, userId)` | emissão de token |
| `AddAsync(TenantMembership)` / `SaveChangesAsync` | inclusão, suspensão, reativação |
| `RemoveAsync(tenantId, userId)` | remoção — apaga também as atribuições de perfil da pessoa **naquele tenant**, na mesma transação |

Remover sem apagar os perfis deixaria a pessoa com perfil num tenant sem vínculo; reincluí-la meses depois devolveria acesso esquecido (mesma lição do "perfil recriado" da ADR-0037).

## Emissão de token

Novo componente `LoginTenantResolver` em `Api/Identity` (substitui o `TenantMembership` do spike):

1. **Tenant do login.** No authorize: `OidcApplication.TenantId` do `client_id` da requisição, ou `user.TenantId` se nulo. Na renovação/troca do code: a claim `tenant_id` do principal que o próprio OpenIddict validou; sem a claim (operador), `user.TenantId`.
2. **Condições para emitir** (todas, a cada emissão):
   - `AccountStateGuard` atual (pode logar, não bloqueado, tenant **dono** ativo) — inalterado;
   - tenant do login ativo (quando difere do dono);
   - vínculo **ativo** no tenant do login.
3. **Perfis:** consulta `UserRoles ⨝ Roles` com `role.TenantId == tenantDoLogin`. Substitui o `GetRolesAsync` nos dois endpoints.
4. **Recusa:** no authorize, sem vínculo ativo ou tenant do login inativo → `access_denied` ao `redirect_uri` ("A conta não tem acesso a esta aplicação."), **sem** `SignOutAsync` — a sessão da pessoa continua valendo nas outras aplicações. Conta bloqueada/tenant dono inativo segue o caminho atual (encerra o cookie e desafia de novo). Na renovação, qualquer falha → `invalid_grant` com a mensagem atual.

`InstallationOperatorPolicy.IsInstallationOperator(user, roles, loginTenantId)`: `loginTenantId == plataforma && user.TenantId == plataforma && roles contém o perfil`. Sem parâmetro opcional: todos os chamadores passam o tenant. `FilterScopes` e `OperatorTwoFactorPolicy.RequiresEnrollment` idem. `OidcPrincipalBuilder.ForUser(user, roles, scopes, resources, loginTenantId)` grava `tenant_id = loginTenantId`.

`TwoFactor.cshtml.cs` (página da conta): perfis do **tenant dono** (é a pessoa decidindo sobre o próprio segundo fator, e o que importa é ser operador).

`UserInfo`, `Logout`, elevação e `GetSessionVersion`: sem mudança. O logout encerra a sessão da pessoa no SecureGate (todas as aplicações perdem o SSO, cada uma mantém seus tokens até expirar) — comportamento de hoje, agora visível.

## Gestão de vínculos

`MembershipEndpoints`, grupo `/api/v1/tenants/{tenantId:guid}/members`, `securegate:admin`, tag `Members`:

| Operação | Rota | `operationId` | Respostas |
| --- | --- | --- | --- |
| Incluir por e-mail | `POST /` corpo `{ "email" }` | `AddTenantMember` | `201` + `TenantMemberDto`; `400` e-mail inválido; `404` tenant ou pessoa inexistente; `409` já é membro (ativo ou suspenso) |
| Incluir ou reativar | `PUT /{userId}` | `SetTenantMember` | `204` (idempotente); `404` tenant ou pessoa |
| Suspender | `POST /{userId}/suspend` | `SuspendTenantMember` | `204` (idempotente); `404` não membro; `409` vínculo do dono |
| Remover | `DELETE /{userId}` | `RemoveTenantMember` | `204` (idempotente: não membro também é `204`); `404` pessoa inexistente; `409` vínculo do dono |

Regras:
- **Tenant de plataforma não aceita membro de outro dono** (`409`): operador é quem a plataforma é dona; um vínculo de plataforma para pessoa de outro tenant não daria nada hoje e seria uma armadilha para o futuro.
- Tenant inexistente → `404`; tenant **inativo** aceita gestão de vínculo (é como o admin prepara a reativação).
- Suspender ou remover **de fato** (não a chamada idempotente sem efeito) chama `ISessionRevoker.RevokeAllAsync(userId, SessionRevocationReason.MembershipEnded)` — valor novo no enum.
- A pessoa não se suspende nem se remove do próprio vínculo (`CallerSubject`, como `CannotRemoveSelfFromOperator`) — evita o admin trancar a si mesmo para fora do tenant de onde administra.
- Erros em `SecureGateErrors.Members`: `UserNotFound`, `AlreadyMember`, `OwnerMembership`, `PlatformTenantForeignUser`, `CannotChangeOwnMembership`, `EmailInvalid`.
- E-mail: mesma validação de `CreateUserHandler`; comparação pelo e-mail normalizado. O `404` de pessoa inexistente é aceitável: só `securegate:admin` alcança a rota (já cria usuário e lista tenants inteiros).

`TenantMemberDto(Guid UserId, string Email, string? DisplayName, Guid OwnerTenantId, bool IsActive, DateTimeOffset CreatedAt)`.

## Rotas de usuário existentes

**Passam a valer para membro (vínculo ativo ou suspenso) do tenant da rota:**
- `GET /users` (`ListUsers`): dono **e** membros; perfis filtrados por `role.TenantId == tenantId` (corrige o defeito acima). `UserDto` ganha no fim `string Membership` (`Owner` | `Member` | `Suspended`) e `Guid? OwnerTenantId` — o record é posicional, campos novos no fim.
- `GET /users/{userId}` (`GetUser`): idem; `UserDetailDto` ganha no fim `string Membership`. Para membro não dono, os campos de credencial (`HasPassword`, `LocalLoginEnabled`, `TwoFactorEnabled`, `ExternalLogins`) saem **vazios/false**: são da pessoa, e quem administra o vínculo não precisa deles.
- `POST /users/{userId}/roles/{role}` e `DELETE ...` (`AddUserRole`, `RemoveUserRole`): exigem vínculo (ativo ou suspenso) no tenant; sem vínculo → `409` `SecureGateErrors.Members.NotMember`.

**Continuam só do tenant dono (sem mudança de comportamento, agora documentado):** criar, convite, reset de senha, login local, nome de exibição, reset do segundo fator, login externo, ativar/desativar, encerrar sessões, elevação. `CreateUser` passa a criar o vínculo do dono na mesma transação.

## Client interativo vinculado a tenant

`ProductClientCommand` ganha no fim `string? Type` (`client_credentials`, padrão quando nulo, | `authorization_code`) e `IReadOnlyList<string>? RedirectUris`, `IReadOnlyList<string>? PostLogoutRedirectUris`. `ProductClientDto`/`CreatedProductClientDto` ganham os mesmos três campos no fim.

Para `authorization_code`:
- confidencial (secret gerado, exibido uma vez — como hoje), PKCE obrigatório (`ft:pkce`), consentimento implícito;
- permissões OpenIddict: endpoints authorization, token e end-session; grants `authorization_code` e `refresh_token`; response type `code`;
- escopos: os de produto (`IsProductScope`) mais `openid`, `profile`, `email`, `roles`, `offline_access`;
- 1–10 redirect URIs, absolutas, sem fragmento, **HTTPS fora de Development** (mesma regra da configuração de plataforma, ADR-0037); post-logout opcional, mesma regra;
- `Roles` (papéis de máquina) devem vir vazios (`400`): a pessoa leva os próprios perfis.
- `Type` não muda no `PUT` (`400` se diferente): mudar de máquina para interativo é outro client.

Na emissão por `client_credentials`, client `authorization_code` não chega (o OpenIddict recusa pela permissão de grant). Nenhuma mudança na restrição `ck_oidc_applications_origin_tenant`.

## SDK, produtos, client NSwag

- Token sem mudança de formato: SDK e produtos não mudam.
- `openapi.json` do SecureGate regenerado; `Secco.SecureGate.Client` ganha os 4 métodos de vínculo e os campos novos — **minor 0.17.0**, aditivo.
- AdminPortal (incluído pelo dono do produto na aprovação, apesar de a ADR-0040 tê-lo listado como fora):
  - lista de usuários do tenant com a coluna de vínculo (`Dono` / `Membro` / `Suspenso`);
  - página do usuário esconde as ações de pessoa quando ele não é dono do tenant da página, e mostra o tenant dono;
  - seção **Membros** na página do tenant: incluir por e-mail, suspender, reativar e remover (com confirmação, avisando que as sessões da pessoa são encerradas);
  - o AdminPortal não tem tela de clients do tenant (ADR-0037 a deixou fora), então o client interativo continua só pela API.

## Testes

**Integração (SecureGate), com bateria negativa provada por mutação:**
1. Mesma pessoa, duas aplicações: SSO sem tela, `tenant_id` e perfis de cada tenant (o cenário do spike).
2. Perfil de outro tenant nunca no token (mutação: trocar o filtro por `GetRolesAsync`).
3. Operador membro de outro tenant com homônimo do perfil: token comum, sem `securegate:admin` (mutação: tirar o `loginTenantId` da política).
4. Sem vínculo → `access_denied`; sessão continua valendo em outra aplicação.
5. Vínculo suspenso e removido: renovação `invalid_grant`; sessão revogada (`sver` muda).
6. Tenant do login desativado: sem token naquela aplicação; outras seguem.
7. Tenant dono desativado: sem token em nenhuma.
8. Renovação mantém o tenant emitido (não aceita trocar de tenant por parâmetro).
9. Operação de pessoa pela rota de tenant não dono → `404` (uma por rota de pessoa).
10. Vínculos: `409` em dono, em plataforma para pessoa de fora, em duplicata e em si mesmo; `DELETE` apaga os perfis do tenant; reincluir não devolve perfil.
11. Atribuir perfil a não membro → `409`.
12. `ListUsers` só com perfis do tenant da rota (o defeito existente).
13. Client `authorization_code` pela API: criado com PKCE; HTTPS exigido fora de DEV; papéis recusados; `Type` imutável; login por ele emite o tenant do vínculo.
14. Migration: usuário existente ganha o vínculo do dono (teste de migration no padrão de `SecureGateMigrationTests`).

**Unitários:** `TenantMembership` (construtor, suspender/reativar), regras de validação do client interativo, `LoginTenantResolver`.

**Regressão:** a suíte inteira tem que passar sem mexer nos testes existentes de login — a prova de que "um sistema só não muda nada".

## Segurança (ADR-0020)

- **Escalada por homônimo** (perfil e operador): fechada pelo filtro de tenant e pela política com `loginTenantId`; ambos com teste de mutação.
- **Tomada de conta por admin de outro tenant:** operações de pessoa só pelo dono; teste por rota.
- **Escolha de tenant pelo chamador:** inexistente — o tenant vem do registro do client ou do token emitido pelo próprio SecureGate, nunca de parâmetro.
- **Enumeração por e-mail** no `AddTenantMember`: restrita a `securegate:admin`, que já lista e cria usuários.
- **Redirect URI aberto** no client interativo: lista fechada por client, HTTPS fora de DEV, sem curinga — validação do próprio OpenIddict no authorize.
- **Revogação:** suspender/remover usa o mecanismo existente (`SecurityStamp` + tokens OpenIddict); tokens de acesso já emitidos vivem até 5 minutos, como em toda revogação hoje (ADR-0032).
- Sem dependência nova.

## Fora desta entrega

Seletor de tenant para aplicação de vários tenants; `sver` por vínculo; diretório federado por vínculo; transferir a conta de dono; client público.
