# Gestão de perfis de acesso — design

**Data:** 2026-09-16
**Status:** aprovado para planejamento
**Item de origem:** issue [#26](https://github.com/rafsecco/secco-platform/issues/26) (`adopter-demand`) —
o SecureGate não permite atribuir nem revogar perfil de um usuário que já existe. Ampliada na conversa
de design para o **administrador completo de perfis e autorização** sobre o ASP.NET Identity.

## Contexto

O Inventário do `secco-intranet` precisa que um admin conceda acesso a usuários específicos ao longo do
tempo. Hoje isso só acontece **na criação** do usuário (`POST /api/v1/tenants/{tenantId}/users`, campo
`roles`); não há rota para mudar depois. O AdminPortal tem a mesma lacuna. Recriar o usuário foi recusado
na própria issue: troca o `Id` (quebra referências por Guid em outros produtos), perde a senha e a sessão.

### O modelo já existe: perfil = Role do Identity

A ADR-0021 fixou "Role é o perfil, Permission é a ação concreta". O Identity já guarda os três elos, nas
tabelas que o SecureGate já tem:

| Tabela | Identity | Plataforma |
| --- | --- | --- |
| `tb_roles` | Role | **Perfil**, por tenant |
| `tb_role_claims` | claims do role | **Permissões** do perfil (`recurso:ação`) |
| `tb_user_roles` | usuário ↔ role | **Membros** do perfil |

O que falta é o **ciclo de vida**, não o modelo. Por isso esta entrega **não tem tabela nova, migração
nem ADR** — a ADR-0021 fica intacta, o token segue levando só `role`, e produtos e SDK não mudam.

## Decisões desta rodada

1. **Dois níveis, nativo do Identity:** usuário → perfil → permissões. Descartado o aninhamento
   usuário → perfil → roles → permissões (sugerido num comentário da #26): exigiria tabela nova ou
   simular grupos em `tb_role_claims` (sem integridade referencial, `tb_roles` misturando dois conceitos),
   migração, expansão de roles no token e ADR — para um ganho que só existe se o mesmo pacote de roles
   se repete muito, o que não é o caso hoje. Quem acumula acesso entra em vários perfis.
2. **Acesso só via perfil** — automático no modelo nativo. A ADR-0001 do `secco-intranet` (setor →
   perfis `{slug}-admin`/`{slug}-user`) segue válida sem revisão.
3. **Entra ID/AD só autentica** (ADR-0026 intacta): é opcional por tenant, nunca decide acesso; tenant sem
   federação tem usuário local criado pelo admin.
4. **Na API o termo continua `roles`** (vocabulário do Identity, da ADR-0021 e do contrato publicado); nas
   telas, **"Perfil"**.
5. **`CreateUser` mantém o campo `roles`** (perfis iniciais): removê-lo quebraria o contrato. Os dois
   caminhos de atribuição passam pelas **mesmas validações**.
6. **Perfil não se renomeia.** O nome é contrato: a Intranet checa `intranet-admin` e `inventario-admin`
   pelo nome, e os produtos fazem cache por `(tenant, nome)`. "Renomear" é criar o novo, mover membros e
   excluir o antigo.
7. **Defeito encontrado e corrigido nesta entrega** (achado próprio, sem issue): na renovação de token os
   roles são re-derivados, mas os **scopes são copiados do token anterior**
   (`TokenEndpoints.HandleUserGrantAsync`), e o filtro que só concede `securegate:admin` a operador roda
   apenas no `/connect/authorize`. Um operador retirado do perfil `installation-operator` seguiria
   renovando `securegate:admin` indefinidamente (refresh deslizante). Inalcançável até hoje, porque não
   havia como retirar ninguém de perfil; esta entrega o tornaria alcançável no primeiro uso.

## Escopo

| Área | Já existe | Entra |
| --- | --- | --- |
| Perfil | criar, listar, definir permissões | ver um perfil, excluir |
| Membros | só na criação do usuário | adicionar, remover, listar membros |
| Usuário | criar, listar, desativar/ativar | detalhe com estado, perfis e permissões efetivas; `status` na listagem |
| AdminPortal | usuários (lista/criar), roles (lista/permissões) | página de perfil, página de usuário, situação na lista, perfis por seleção |

## Regras de segurança

### Autorização e isolamento

- Tudo atrás de `securegate:admin`, como o resto da gestão.
- Usuário e perfil são resolvidos **dentro do tenant da rota**; de outro tenant respondem **404, idêntico
  a inexistente** (sem enumeração).
- Perfil é localizado sempre por `(tenant, nome normalizado)`. **Proibidos** no SecureGate:
  `RoleManager.FindByNameAsync`, `UserManager.AddToRoleAsync`, `RemoveFromRoleAsync` e `IsInRoleAsync` —
  ignoram o tenant e alcançariam o perfil homônimo de outro tenant.
- O nome do perfil na rota passa por `RoleInputRules.IsValidName` antes de qualquer consulta.

### Perfis reservados

- `installation-operator`: existe só no tenant de plataforma, logo só é atribuível lá. **Nunca pode ser
  excluído.** Permissões já são bloqueadas pela API.
- `installation-log-reader`, `installation-auditor` e `platform-operator` (legado) são identidades **só de
  token** (ADR-0031): **nunca atribuíveis a usuário**, nem na criação nem pela atribuição.

### Guardas contra perda de acesso (409)

- Ninguém se remove do perfil `installation-operator` (comparando `sub` do chamador).
- Não se remove **o último operador ativo** — conta só membro não desativado nem bloqueado.
- A mesma regra passa a valer para **desativar** o último operador ativo: um client de máquina com
  `securegate:admin` (ex.: admin da Intranet) não é operador, e a guarda de auto-desativação da 0.6.0 não
  o cobre.
- Perfil com membros não pode ser excluído (esvaziar é ato explícito; exclusão acidental não retira acesso
  de muitos num clique irreversível).

### Propagação

- Mudança de membros: vale no próximo login ou renovação — em até um TTL de access token.
- Mudança de permissões: vale no cache dos produtos, em até o TTL da ADR-0021 (inalterado).
- **Correção do defeito:** a renovação passa a **reaplicar o filtro de operador** aos scopes, pela mesma
  função do `/connect/authorize` (extraída, não duplicada). Quem perde o perfil perde `securegate:admin` e
  volta a ter `tenant_id` na renovação seguinte.

### Fora desta entrega

- **Auditoria de mudanças de acesso.** Exige o SecureGate escrevendo no LogStream como identidade de
  auditoria. Quando vier, será **opcional por configuração** e, desligada, **não bloqueia** a gestão — ao
  contrário da elevação (ADR-0031), que recusa sem auditoria por ser privilégio cross-tenant.

## API

Sob `/api/v1/tenants/{tenantId}`, `securegate:admin`.

### Perfis (`/roles`)

| Operação | Rota | Sucesso | Erros |
| --- | --- | --- | --- |
| `GetRole` | `GET /roles/{role}` | 200 `{ name, permissions, isReserved, memberCount }` | 400 nome inválido, 404 |
| `DeleteRole` | `DELETE /roles/{role}` | 204 | 400, 404, 409 reservado, 409 tem membros |
| `ListRoleMembers` | `GET /roles/{role}/members?page&size` | 200 `PagedResult` de `{ userId, email, status }`, tamanho limitado pelo SharedKernel (200) | 400, 404 |

### Usuários (`/users`)

| Operação | Rota | Sucesso | Erros |
| --- | --- | --- | --- |
| `GetUser` | `GET /users/{userId}` | 200 detalhe | 404 (inexistente ou de outro tenant) |
| `AddUserRole` | `POST /users/{userId}/roles/{role}` | 204, idempotente | 400, 404, 400 não atribuível |
| `RemoveUserRole` | `DELETE /users/{userId}/roles/{role}` | 204, idempotente | 400, 404, 409 remover a si do operador, 409 último operador ativo |
| `DeactivateUser` *(existe)* | `POST /users/{userId}/deactivate` | 204 | + 409 último operador ativo |
| `CreateUser` *(existe)* | `POST /users` | 201 | + 400 perfil não atribuível em `roles` |

Detalhe do usuário:

```json
{
  "id": "…",
  "email": "maria@empresa.com",
  "tenantId": "…",
  "status": "Active | Deactivated | LockedOut",
  "lockoutEnd": null,
  "roles": ["financeiro-user", "inventario-admin"],
  "effectivePermissions": ["documentos:read", "inventario:write"],
  "externalLogins": ["EntraId"]
}
```

- `status`: `Deactivated` é lockout sem fim (`DateTimeOffset.MaxValue`, gravado pela desativação);
  `LockedOut` é lockout temporário por tentativas, com `lockoutEnd`.
- `effectivePermissions`: calculadas pela **mesma resolução** que os produtos consultam
  (`GetRolePermissionsHandler`, com os casos especiais de perfil reservado) — nunca por uma segunda lógica.
- `externalLogins`: só o nome do provedor; o identificador do diretório (`tid:oid`) nunca sai.
- `UserDto` da listagem ganha `status` (aditivo).
- **Idempotência:** atribuir ou remover repetido devolve 204; 404 significa sempre "perfil ou usuário não
  existe", nunca "já estava assim".
- **Paginação só em membros:** endpoint novo nasce paginado; `ListUsers` fica como está para não quebrar
  contrato.

## Telas do AdminPortal

- **Página do tenant** (`/tenants/{id}`): coluna Situação na lista de usuários, e-mail com link para o
  detalhe; criar usuário com **perfis por seleção** (fim do texto livre por vírgula; não atribuíveis não
  aparecem); seção renomeada para **Perfis**, com link para cada um.
- **Perfil** (`/tenants/{id}/roles/{perfil}`, nova): permissões (edição movida para cá), membros paginados
  com adicionar (dentre os usuários do tenant) e remover, excluir perfil.
- **Usuário** (`/tenants/{id}/users/{userId}`, nova): situação, perfis com adicionar/remover, permissões
  efetivas, logins externos, desativar/ativar.
- Ações destrutivas pedem confirmação. Botões desabilitados são conveniência; a regra é do servidor, e a
  mensagem do 409 é exibida via `ApiErrorFormatter`.

## Testes

### SecureGate — integração por HTTP com tokens emitidos de verdade

Positivos:

- Atribuir perfil → o role aparece no token da renovação; remover → some na renovação.
- Detalhe do usuário nos três estados.
- `effectivePermissions` igual ao que o endpoint de resolução de permissões devolve.
- Paginação de membros.

Negativos:

- 401 e 403 em **todos** os endpoints novos (teoria parametrizada).
- Usuário ou perfil de outro tenant → 404.
- Nome de perfil inválido → 400.
- Perfil só de token não atribuível, **nos dois caminhos** (criar e atribuir).
- Excluir reservado → 409; excluir com membros → 409.
- Remover a si do operador → 409.
- Remover e desativar o último operador ativo → 409, **executado por client de máquina** com
  `securegate:admin`.
- **Defeito corrigido:** operador retirado do perfil → a renovação seguinte não traz `securegate:admin` e
  traz `tenant_id`.

Guarda de API do Identity: teste que varre o código-fonte do SecureGate e falha ao encontrar as quatro
chamadas proibidas. Sem biblioteca nova — o repositório não tem ferramenta de teste de arquitetura, e
trazer uma pediria análise de dependência (ADR-0020) por pouco ganho.

Mutação: cada guarda de segurança é quebrada de propósito, e o teste que a protege tem de falhar.

### AdminPortal

Testes dos serviços no padrão de `IdentityAdminServicesTests`: chamadas novas e tradução de 404/409 em
mensagem. Sem teste de componente (o repositório não usa bUnit).

## Entrega

- Contrato regenerado e `Secco.SecureGate.Client` **0.7.0** (aditivo), com a cadeia do MinVer.
- CHANGELOG e roadmap atualizados.
- Issue #26 fechada com comentário explicando a decisão por perfil = role nativo, e por que a camada de
  grupo sugerida ficou de fora.

## Próximas entregas (decomposição acordada)

1. **Esta** — perfis.
2. **Gestão de conta:** admin redefine senha com troca obrigatória e encerrar sessões; **editar e-mail**, com
   restrição para conta ainda **sem** vínculo Entra num tenant federado (o e-mail decide quem casa no
   primeiro login federado); **desvincular login Entra**, para conta **com** vínculo cujo `oid` mudou ou foi
   vinculado errado — o próximo login pelo Entra volta a casar por e-mail e grava o vínculo novo. O login
   federado da ADR-0026 segue inalterado em ambos.
3. **Senha self-service:** trocar a própria senha e "esqueci minha senha" por e-mail — primeira dependência
   SecureGate → NotificationHub (ADR).
4. **Segundo fator (2FA) no login local** (ADR).
