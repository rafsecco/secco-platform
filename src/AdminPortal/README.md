# Secco.AdminPortal

Console de operação da plataforma Secco (Fase 7, **ADR-0023**). É o primeiro produto que **não é uma API de quatro camadas**: uma aplicação **Blazor Server** que atua como **relying party OIDC** e orquestra os demais produtos via seus clients NSwag (ADR-0006). Não tem domínio nem banco próprios.

## Arquitetura (ADR-0023)

- **Blazor Server** (render interativo por circuito) — C# ponta a ponta, reuso direto dos `Secco.*.Client`.
- **Relying party OIDC**, não resource server: autentica via **authorization code + PKCE** contra o SecureGate (Fase 6.5), com sessão em **cookie**. Não usa `AddSeccoAuthentication()` (isso é para APIs que validam JWT); reusa apenas o cross-cutting não-de-auth do SDK (correlation, resilience, health).
- **On-behalf-of**: chama as APIs de produto com o **access token do operador** (um `IOperatorTokenProvider` lê o token custodiado como claim no cookie de sessão e o anexa às chamadas). Cada ação carrega a identidade e as permissões reais do operador; a auditoria é a pessoa, não o AdminPortal.
- **Operador cross-tenant**: o usuário do AdminPortal é o operador de plataforma — um usuário com o role `platform-operator` no tenant de plataforma. O SecureGate **filtra o scope `securegate:admin` no login** (só operadores o recebem, ADR-0020): login de usuário comum não escala para admin. As telas exigem a policy `Operator` (`RequireRole("platform-operator")`).

## O que está entregue

**7.1 — fundação**
- Login OIDC + shell autenticado (layout, navegação, badge do usuário, logout) — exercita o authorization code/PKCE da 6.5.
- Fatia vertical de **gestão de tenants**: a página `/tenants` lista os tenants via `Secco.SecureGate.Client` on-behalf-of o operador.
- Liveness/readiness anônimos (`/health/live`, `/health/ready`).

**7.2 — administração de identidade**
- Drill-in da lista de tenants para `/tenants/{id}` (página de gestão), com o contexto de tenant explícito na URL.
- Seção **Usuários**: listar + criar (e-mail / senha inicial / roles).
- Seção **Roles & permissões**: listar, criar role, e editar permissões em **texto livre** (`recurso:acao`, uma por linha) — enviadas no PUT idempotente do SecureGate.
- Um `ISecureGateClientFactory` central constrói o client autenticado (anexa o token do operador); erros do client (400/409) são traduzidos em mensagens amigáveis a partir do `detail` do ProblemDetails (ADR-0020).

**7.3 — visualização de logs por tenant (ADR-0024)**
- Página `/tenants/{id}/logs` (drill-in): busca paginada de `log-entries` de um tenant, com filtros de nível e mensagem, via `Secco.LogStream.Client` on-behalf-of o operador.
- O `LogQueryService` anexa o **token do operador** e o header **`X-Tenant-Id`** do tenant alvo. O operador é **tenant-less** no token (ADR-0024): escolhe o tenant por requisição (caminho "sem claim → header" da ADR-0005), e a autorização de leitura vem do **read-set cross-tenant** que o SecureGate resolve para o papel `platform-operator` — somente leitura.

**7.4 — gestão de bancos de tenant**
- Seção **Bancos** na página do tenant: lista os produtos que têm banco (a partir de `Products`) e cadastra/rotaciona a connection string via o PUT idempotente do SecureGate (produto em texto livre + input password).
- **Write-only** (ADR-0020): a connection string nunca é exibida em nenhuma leitura — só se envia.

## Configuração (`Secco:SecureGate`)

| Chave | Uso |
|---|---|
| `Authority` | URL do SecureGate (issuer OIDC) — discovery/JWKS |
| `ApiBaseUrl` | Base da API do SecureGate (padrão = `Authority`) |
| `ClientId` / `ClientSecret` | Client confidencial do AdminPortal (`secco-adminportal`) |

> Em DEV, `Authority`/`ApiBaseUrl` já apontam para `https://localhost:4001`, a porta do SecureGate
> (o seed de DEV registra o client `secco-adminportal` e o operador `operador@secco.local`).
>
> **O AdminPortal precisa rodar em `https://localhost:5001`**: é o redirect URI gravado no client
> pelo seed. O SecureGate, por sua vez, precisa subir em modo *self-issued* — ele valida HS256 por
> padrão e não aceitaria os tokens que ele mesmo emite. O compound
> **"Plataforma: AdminPortal + SecureGate + LogStream"** do [`.vscode/launch.json`](../../.vscode/launch.json)
> sobe os três já configurados; é o caminho recomendado.

## Testes

`tests/AdminPortal/Secco.AdminPortal.Tests` (ADR-0012): `OperatorTokenProvider` (custódia do token no principal), `SecureGateClientFactory` (encaminhamento do token on-behalf-of), serviços de tenant/usuário/role (projeção dos DTOs, client substituído) e smoke de composição (o app sobe e expõe o liveness sem depender do SecureGate no ar).
