# Secco.AdminPortal

Console de operação da plataforma Secco (Fase 7, **ADR-0023**). É o primeiro produto que **não é uma API de quatro camadas**: uma aplicação **Blazor Server** que atua como **relying party OIDC** e orquestra os demais produtos via seus clients NSwag (ADR-0006). Não tem domínio nem banco próprios.

> **Papel redefinido em 2026-09-04** ([issue #4](https://github.com/rafsecco/secco-platform/issues/4)): este é o **console mínimo para quem adota a plataforma sem um portal próprio**. Cada instalação da plataforma é soberana — a empresa que adota pode ter o próprio portal (é o caso do `secco-intranet`, que será o portal completo de quem o usa) e, nesse caso, o AdminPortal deixa de ser o caminho principal. Ele existe porque a promessa da plataforma é que **SecureGate e LogStream sejam adotáveis isoladamente**: quem implanta só o SecureGate ainda precisa administrá-lo. A duplicação de telas com um portal de adotante é consciente e é o preço dessa promessa.
>
> **Revisto em 2026-10-21 (ADR-0051):** a centralização na Intranet foi abandonada. O AdminPortal é *o* console de operação da instalação, com ou sem portal de adotante; um portal como a Intranet administra só o próprio tenant.
>
> No mesmo movimento, o vocabulário: o papel passou a ser `installation-operator` — **operador desta instalação**, não operador da Secco (ADR-0030). O seed de referência converge o nome antigo renomeando a linha no lugar, o que preserva o `Id` do papel e, com ele, as atribuições de usuário.

## Arquitetura (ADR-0023)

- **Blazor Server** (render interativo por circuito) — C# ponta a ponta, reuso direto dos `Secco.*.Client`.
- **Relying party OIDC**, não resource server: autentica via **authorization code + PKCE** contra o SecureGate (Fase 6.5), com sessão em **cookie**. Não usa `AddSeccoAuthentication()` (isso é para APIs que validam JWT); reusa apenas o cross-cutting não-de-auth do SDK (correlation, resilience, health).
- **On-behalf-of**: chama as APIs de produto com o **access token do operador**. O token fica num **cofre no servidor** do [`Secco.SDK.RelyingParty`](../SDK/Secco.SDK.RelyingParty/README.md) (ADR-0046): o cookie leva só o id da sessão, a renovação acontece sob demanda com refresh rotacionado, e o `ISeccoUserContext` (implementado por `BlazorSeccoUserContext`) dá acesso ao token por circuito. Cada ação carrega a identidade e as permissões reais do operador; a auditoria é a pessoa, não o AdminPortal.
- **Operador cross-tenant**: o usuário do AdminPortal é o operador de instalação — um usuário com o role `installation-operator` no tenant de plataforma. O SecureGate **filtra o scope `securegate:admin` no login** (só operadores o recebem, ADR-0020): login de usuário comum não escala para admin. As telas exigem a policy `Operator` (`RequireRole("installation-operator")`).

## O que está entregue

**7.1 — fundação**
- Login OIDC + shell autenticado (layout, navegação, badge do usuário, logout) — exercita o authorization code/PKCE da 6.5.
- Fatia vertical de **gestão de tenants**: a página `/tenants` lista os tenants via `Secco.SecureGate.Client` on-behalf-of o operador.
- Liveness/readiness anônimos (`/health/live`, `/health/ready`).

**7.2 — administração de identidade**
- Drill-in da lista de tenants para `/tenants/{id}` (página de gestão), com o contexto de tenant explícito na URL.
- Seção **Usuários**: listar + criar. Desde a ADR-0033 o operador **não escolhe senha de ninguém**: a conta nasce com convite por e-mail (senha local) ou só com o diretório corporativo.
- Seção **Roles & permissões**: listar, criar role e editar permissões — hoje escolhidas no catálogo de permissões dos produtos (ver "Perfis e catálogo" abaixo), enviadas no PUT idempotente do SecureGate.
- Um `ISecureGateClientFactory` central constrói o client autenticado (anexa o token do operador); erros do client (400/409) são traduzidos em mensagens amigáveis a partir do `detail` do ProblemDetails (ADR-0020).

**7.3 — visualização de logs por tenant (ADR-0024)**
- Página `/tenants/{id}/logs` (drill-in): busca paginada de `log-entries` de um tenant, com filtros de nível e mensagem, via `Secco.LogStream.Client` on-behalf-of o operador.
- O `LogQueryService` anexa o **token do operador** e o header **`X-Tenant-Id`** do tenant alvo. O operador é **tenant-less** no token (ADR-0024): escolhe o tenant por requisição (caminho "sem claim → header" da ADR-0005), e a autorização de leitura vem do **read-set cross-tenant** que o SecureGate resolve para o papel `installation-operator` (antes `platform-operator`) — somente leitura.

**7.4 — gestão de bancos de tenant**
- Seção **Bancos** na página do tenant: lista os produtos que têm banco (a partir de `Products`) e cadastra/rotaciona a connection string via o PUT idempotente do SecureGate (produto em texto livre + input password).
- **Write-only** (ADR-0020): a connection string nunca é exibida em nenhuma leitura — só se envia.

**Admins de tenant (ADR-0044, issue #37)**
- Seção **Admins do tenant** na página do tenant: quem administra o tenant on-behalf-of si mesmo (a pessoa pode ser de qualquer tenant), com revogação confirmada.
- Seção **Administração de tenants** na página da pessoa: os tenants que ela administra (revogar) e a concessão de um novo. Não há rota "por pessoa" para o operador: o portal consulta os admins de cada tenant do catálogo.
- As duas seções são independentes do resto da página: um SecureGate anterior à ADR-0044 mostra o erro só nelas.

**Criar tenant (ADR-0051)**
- Formulário **Novo tenant** (nome + slug) na lista `/tenants`, via `POST /api/v1/tenants` on-behalf-of o operador; leva à página do tenant criado. Nasce ativo, sem bancos e sem admin de tenant. Validação e slug repetido (`409`) vêm do SecureGate.

**Federação (ADR-0026, ADR-0052)**
- Seção **Federação (Entra ID)** na página do tenant: mostra directory id, situação, domínios de e-mail permitidos e última alteração, e configura via o PUT idempotente do SecureGate. Federação é só do operador (ADR-0052): o admin de tenant não tem esta rota.
- Seção **Grupos do diretório** (ADR-0036/0055): mapeamentos grupo do Entra → perfil e a marca de **grupo de provisionamento** (um por tenant, com confirmação — marcar passa ao diretório do cliente a decisão de quem tem conta). A página da pessoa indica conta gerida pelo diretório e a origem da desativação (`Manual`/`Directory`).

**Pessoas (`/tenants/{id}/users/{userId}`)**
- Perfis da pessoa (atribuir e retirar — retirar encerra as sessões, ADR-0032), **acesso efetivo** e administração de tenants (ADR-0044).
- Credencial (ADR-0033): reenviar convite, redefinir senha (encerra as sessões na hora), ligar/desligar a senha local, resetar o segundo fator; vínculo com o diretório (desvincular, ADR-0026); ativar/desativar a conta (desativar encerra as sessões).
- Operações de credencial e conta só aparecem no **tenant dono** da pessoa (ADR-0040).

**Membros (ADR-0040)**
- Seção **Membros** na página do tenant: incluir pessoa que já tem conta em outro tenant (pelo e-mail), suspender e remover o vínculo — as duas últimas encerram as sessões da pessoa em todos os sistemas.

**Perfis e catálogo (ADR-0039, ADR-0043, ADR-0056)**
- Página do perfil (`/tenants/{id}/roles/{role}`): permissões escolhidas no catálogo publicado pelos produtos (inclusive famílias como `setor-{param}:read`), permissões órfãs e limpeza, membros, exclusão.
- Página **Catálogo de permissões** (`/permission-catalog`): o catálogo de cada produto.
- O perfil implícito `tenant-member` aparece como "todos os membros ativos": permissões editáveis, sem lista de membros, sem exclusão e fora dos seletores de perfil atribuível.

**Trilha (ADR-0054)**
- Seção **Trilha** na página do tenant: o que as aplicações com `securegate:tenant-manage` escreveram no tenant, gravado pelo SecureGate antes de cada operação. Só o operador lê.

**Ainda não tem tela** (a API existe, sob `securegate:admin` — ver o backlog do [roadmap](../../docs/roadmap.md)): ativar/desativar tenant, provisionamento de banco (ADR-0028) e clients de produto (ADR-0037).

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

`tests/AdminPortal/Secco.AdminPortal.Tests` (ADR-0012): `SecureGateClientFactory` (encaminhamento do token on-behalf-of), serviços de tenant/usuário/role/concessão (projeção dos DTOs, client substituído), consulta de logs (token + `X-Tenant-Id`), catálogo e entrada de famílias de permissão, provisionamento pelo diretório, trilha e smoke de composição (o app sobe e expõe o liveness sem depender do SecureGate no ar). A custódia do token é testada no próprio `Secco.SDK.RelyingParty`.
