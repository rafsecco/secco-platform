# Administração do próprio tenant por client de máquina — design

> **Implementada em 2026-10-22** — ADR-0052; SecureGate `3798855`/`873b0a9`, client `d4aa692` (`Secco.SecureGate.Client` 0.21.0), AdminPortal `93e6af7`, Intranet `b5840a7`.

**Data:** 2026-10-21 · **Origem:** ADR-0051 (fim da centralização) deixou a Intranet administrando o próprio tenant com o client de plataforma que tem `securegate:admin` · **ADR a escrever:** ADR-0052

## Intenção

Hoje o `secco-intranet` cria perfis de setor, lista pessoas, atribui e retira perfis, desativa contas e encerra sessões **do próprio tenant** com um client de plataforma (sem tenant) que tem `securegate:admin`. Esse escopo alcança a instalação inteira: quem roubar o secret da Intranet cria tenant, lê e altera pessoas de todos os tenants, gera script de banco com senha. É o achado mais previsível do pentest planejado.

O objetivo é que a credencial de máquina da Intranet só alcance **o tenant dela**, sem exigir segundo fator das pessoas (a saída on-behalf-of da ADR-0044 exigia e foi congelada pela ADR-0051).

## Metas e não-metas

**Metas**
- Um escopo novo, `securegate:tenant-manage`, que abre a um client de máquina **vinculado a tenant** (ADR-0037) um conjunto fechado de rotas administrativas, **só no tenant do próprio token**.
- A Intranet deixa de precisar de `securegate:admin`.

**Não-metas**
- Não muda a ADR-0044 nem a reabre: a autoridade de admin de tenant por pessoa segue congelada.
- Não cria tenant, não ativa/desativa tenant, não provisiona banco, não mexe em federação, clients ou vínculos de outra conta — essas continuam com o operador.
- Sem rota nova e sem mudança de contrato HTTP: as rotas existentes passam a aceitar o escopo novo.

**Anti-metas** (se acontecer, falhou mesmo funcionando)
- Um client com o escopo alcançar outro tenant, o tenant de plataforma, ou uma conta administrativa (operador, admin de tenant, designado).
- Alguém sem `securegate:admin` conseguir emitir uma credencial com o escopo (escalada: admin de tenant criando máquina mais poderosa que ele).
- Rota nova nascer aberta ao escopo sem aparecer em diff de lista versionada.

## Decisões

### 1. Escopo e quem o recebe
- `securegate:tenant-manage`, escopo de **client de produto de máquina** (`client_credentials`, vinculado a tenant) — mesmo molde do `directory:read` (ADR-0045). Client interativo e client de plataforma não o recebem (validador de `SecureGate:PlatformClients` recusa no startup, como já faz com `directory:read`).
- **Só o operador concede.** `CreateProductClient`/`UpdateProductClient` com esse escopo exigem `securegate:admin` no chamador; pela autoridade de admin de tenant (ADR-0044/0048) respondem `403`. Evita a escalada da anti-meta 2.
- Nunca concedível a client do tenant de plataforma.
- Na emissão do token, o SecureGate já re-checa tenant ativo e lista de escopos (ADR-0037); o escopo novo entra nessa lista conferida.

### 2. Rotas abertas ao escopo — lista fechada e versionada
Marca própria (`.AllowTenantManage()`), separada da `.AllowTenantAdmin()`, com lista em `tests/SecureGate/.../Architecture/tenant-manage-routes.txt` imposta por teste de arquitetura (rota marcada sem linha reprova; linha sem rota também). Conteúdo inicial — exatamente o que a Intranet usa hoje, menos federação:

| Rota | Protege conta administrativa |
|---|---|
| `GetTenant` | não |
| `ListUsers`, `GetUser` | não |
| `AddUserRole` | não |
| `RemoveUserRole` | sim |
| `SetUserDisplayName` | sim |
| `DeactivateUser`, `ActivateUser` | sim |
| `RevokeUserSessions` | sim |
| `CreateRole`, `ListRoles`, `GetRole`, `SetRolePermissions`, `DeleteRole` | não |

Fora, de propósito: criação/desativação de tenant, provisionamento, federação, clients, vínculos, convite/senha/2FA/login externo, catálogo de permissões e tudo que não está na tabela.

### 3. Conferência por requisição
Para quem chega com `securegate:tenant-manage` (e sem `securegate:admin`):
1. Tenant da rota **igual** ao `tenant_id` do token; senão `403` (a mesma resposta genérica da ADR-0044 — não diz se o tenant existe).
2. Tenant de plataforma → `403`.
3. Rota com proteção e alvo (`{userId}`) que é conta administrativa (concessão de admin ou de criação, designado, operador — o `IsAdministrativeAccountAsync` existente) → `403`.
4. `AddUserRole`: perfil reservado ou travado continua recusado pelas regras existentes; atribuir `installation-operator` é impossível porque o tenant nunca é o de plataforma.
5. Isolamento por baixo continua: escopo de acesso do tenant (ADR-0041) e RLS.

A conferência reusa as peças da `TenantAdminAuthority` (alvo administrativo, recusa genérica), mas é um caminho próprio: o critério de autoridade aqui é o vínculo do client, não uma concessão de pessoa. `IsTenantAdminCaller` deixa de ser "não tem `securegate:admin`" e passa a distinguir os dois tipos de chamador.

### 4. Trilha
(Confirmado pelo dono, 2026-10-21.) Toda escrita com o escopo é auditada **de forma síncrona e fail-closed** no LogStream do próprio tenant, pelo mesmo caminho da ADR-0044, com o client (`clientId`) como ator. Motivo: a trilha da própria Intranet não vê quem usa o secret roubado direto contra o SecureGate — só a do SecureGate vê. Sem auditoria configurada, escrita com o escopo recusa (`403`); leitura segue.

### 5. Adoção no `secco-intranet`
- Operador registra um client de produto no tenant da Intranet com `securegate:tenant-manage` + `directory:read` (o diretório já pedia client de produto pela ADR-0045).
- A Intranet ganha uma segunda credencial de máquina (seção de configuração própria) para o client administrativo; o client de plataforma dela fica só com `authorization:read`, `permissions:intranet` e os escopos de LogStream/NotificationHub, **sem `securegate:admin`**.
- A federação sai da tela `/acesso` da Intranet: passa a ser só do operador (decisão do dono, 2026-10-21).

### 6. Federação no AdminPortal (nesta entrega)
Como a Intranet perde a tela, o AdminPortal ganha a seção **Federação** na página do tenant: ver, habilitar/desabilitar e definir o directory id do Entra, via as rotas existentes (`GetTenant`, que já traz `federation`, e `UpsertTenantFederation`) on-behalf-of o operador. Fecha o follow-up da ADR-0026 que estava em "decisões adiadas".
- Quebra aceitável no adotante (memória: sem shim), com nota de upgrade.

## Verificação
- Testes de integração: cada rota da lista funciona no próprio tenant; outro tenant, tenant de plataforma e conta administrativa → `403`; rota fora da lista → `403`; `CreateProductClient` com o escopo por admin de tenant → `403`, por operador → `201`; client de plataforma com o escopo → startup recusa; escrita sem auditoria configurada → `403`.
- Bateria de mutação sobre as invariantes acima (padrão das ADR-0031/0044).
- Suíte completa da solution; Intranet: suíte completa + fumaça contra SecureGate local.
- Pacote: `Secco.SecureGate.Client` não muda (contrato igual); só a constante de escopo, se a Intranet a ler de lá.

## Deixado ao implementador
Nomes internos (marca, filtro, arquivo de lista), mensagem de log, ordem dos commits.
