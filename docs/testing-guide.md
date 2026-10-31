# Guia de testes — Secco Platform

Como verificar a plataforma: a suíte inteira de uma vez, projeto por projeto, o template (que fica fora da solution) e o roteiro manual com tudo rodando.

Para adotar a plataforma num produto seu, veja [getting-started.md](getting-started.md). Para a visão de arquitetura, [architecture-overview.md](architecture-overview.md).

## 1. Pré-requisitos

```bash
dotnet --version                 # 10.x
docker info                      # daemon respondendo
dotnet tool restore              # dotnet-ef, usado para migrations
dotnet dev-certs https --trust   # uma vez por máquina (ou task "setup: confiar no certificado de DEV")
```

Os testes de integração sobem **SQL Server real via Testcontainers** (ADR-0012). Sem Docker, só as suítes de unidade passam.

## 2. Tudo de uma vez

```bash
dotnet restore Secco.Platform.slnx
dotnet build Secco.Platform.slnx --configuration Release
dotnet test Secco.Platform.slnx --configuration Release --no-build
```

**Esperado: 2.381 testes em 13 projetos, 0 falhas** (contagem de 2026-10-31). O build deve terminar com **0 erros e nenhum aviso de compilador** — nos projetos de produção `TreatWarningsAsErrors` está ligado, então aviso vira erro; nos `*.Tests` está desligado de propósito, e ali sobram alguns avisos de analisador (CA*), que são conhecidos.

> `dotnet test` na solution roda os projetos **em paralelo**, e cada suíte de integração sobe os próprios containers de SQL Server. Numa máquina folgada isso funciona; numa apertada — ou numa folgada disputando recursos com outros processos pesados —, satura o Docker. O sintoma é falha em massa vinda de `Docker.DotNet` ou de `SeccoSqlServerInstance` (`TaskCanceledException`, `DockerApiException`), nenhuma de asserção, às vezes com testhost que não termina. Não é bug do código; vá para a seção 3 ou 4. Em sequência, as três suítes de integração levam cerca de 4 minutos.

## 3. Parte por parte

Um projeto de cada vez, na ordem do mais barato para o mais caro:

```bash
dotnet test tests/SharedKernel/Secco.SharedKernel.Tests            --configuration Release --no-build
dotnet test tests/SDK/Secco.SDK.EntityFrameworkCore.Tests          --configuration Release --no-build
dotnet test tests/SDK/Secco.SDK.AspNetCore.Tests                   --configuration Release --no-build
dotnet test tests/SDK/Secco.SDK.Testing.Tests                      --configuration Release --no-build
dotnet test tests/SDK/Secco.SDK.ClientCredentials.Tests            --configuration Release --no-build
dotnet test tests/SDK/Secco.SDK.Logging.Tests                      --configuration Release --no-build
dotnet test tests/SDK/Secco.SDK.RelyingParty.Tests                 --configuration Release --no-build
dotnet test tests/SDK/Secco.SDK.Email.Tests                        --configuration Release --no-build
dotnet test tests/SDK/Secco.SDK.Imaging.Tests                      --configuration Release --no-build
dotnet test tests/AdminPortal/Secco.AdminPortal.Tests              --configuration Release --no-build
dotnet test tests/LogStream/Secco.LogStream.Tests                  --configuration Release --no-build
dotnet test tests/SecureGate/Secco.SecureGate.Tests                --configuration Release --no-build
dotnet test tests/NotificationHub/Secco.NotificationHub.Tests      --configuration Release --no-build
```

| Projeto | Testes | Docker? | O que prova |
|---|---:|---|---|
| `Secco.SharedKernel.Tests` | 150 | não | `Result<T>`, paginação, entidades base, claims/permissões e famílias de permissão |
| `Secco.SDK.EntityFrameworkCore.Tests` | 81 | não | convention de nomenclatura (ADR-0017), seeding, seletor de provider, cifragem, migração no primeiro uso |
| `Secco.SDK.AspNetCore.Tests` | 99 | não | correlation, auth, tenancy, autorização, versão de sessão, health, background jobs |
| `Secco.SDK.Testing.Tests` | 29 | **não** | a própria base de testes: modo container vs externo, allowlist de nome de banco, chave aleatória por instância |
| `Secco.SDK.ClientCredentials.Tests` | 12 | não | token de máquina, cache e renovação |
| `Secco.SDK.Logging.Tests` | 23 | não | sink `ILogger`: fila limitada, descarte contado, anti-recursão, lote por tenant |
| `Secco.SDK.RelyingParty.Tests` | 72 | não | cofre do token, renovação, autenticação recente, token trocado, volta só para caminho local |
| `Secco.SDK.Email.Tests` | 7 | não | adaptadores SMTP/SendGrid e validação da configuração |
| `Secco.SDK.Imaging.Tests` | 61 | não | variantes, orientação EXIF, remoção de metadados, limites de entrada |
| `Secco.AdminPortal.Tests` | 88 | não | encaminhamento on-behalf-of, serviços de identidade, catálogo, logs, provisionamento pelo diretório, trilha |
| `Secco.LogStream.Tests` | 120 | **sim** | ingestão, consulta, auditoria síncrona, retenção, isolamento entre tenants, paridade PostgreSQL |
| `Secco.SecureGate.Tests` | 1.542 | **sim** | OIDC, catálogo, autorização, login, credenciais, 2FA, sessões, federação e diretório, RLS, autoridades por mutação, E2E cross-produto |
| `Secco.NotificationHub.Tests` | 97 | **sim** | e-mail assíncrono (Hangfire real), inbox in-app, Teams/Slack, lote, agendamento, paridade PostgreSQL |
| **Total** | **2.381** | | |

Para rodar um teste específico:

```bash
dotnet test tests/LogStream/Secco.LogStream.Tests --filter "FullyQualifiedName~LogRetention"
```

No VS Code, a task **`test: um projeto`** pergunta qual projeto rodar.

## 4. Quando a máquina não aguenta os containers

Duas saídas, da mais simples para a mais robusta:

**(a) Rodar projeto a projeto** — a seção 3. Só três suítes usam Docker, e uma por vez cabe em qualquer máquina.

**(b) Apontar para um SQL Server já de pé** (ADR-0027). Com `SECCO_TEST_SQLSERVER` preenchida, as suítes **não sobem container nenhum** e usam a instância indicada; cada suíte cria bancos com sufixo próprio e os derruba no fim, então duas execuções não colidem.

```bash
docker compose up -d          # sobe o SQL Server local (porta 1433)

# bash
export SECCO_TEST_SQLSERVER="Server=localhost,1433;User Id=sa;Password=Secco@Dev123;TrustServerCertificate=true"
# PowerShell
$env:SECCO_TEST_SQLSERVER = "Server=localhost,1433;User Id=sa;Password=Secco@Dev123;TrustServerCertificate=true"

dotnet test Secco.Platform.slnx --configuration Release --no-build
```

Para voltar ao padrão, apague a variável (`unset SECCO_TEST_SQLSERVER` / `Remove-Item Env:SECCO_TEST_SQLSERVER`). O CI nunca usa esse caminho — lá cada suíte sobe o próprio container, para continuar hermético.

## 5. O template (não está na solution)

`templates/secco-service/` é conteúdo de template: não é compilado pelo `dotnet build` da solution. Pela ADR-0013, divergência entre ele e o padrão é bug de prioridade alta, então ele tem verificação própria — a mesma que o job `validate-template` roda no CI.

O produto gerado é **monorepo-first**: os `ProjectReference` são caminhos relativos para dentro do repositório, então ele **só compila se for instanciado dentro de `src/`**.

```bash
dotnet new install ./templates/secco-service
dotnet new secco-service -n Secco.TemplateSmoke -o src/TemplateSmoke

dotnet tool restore
dotnet restore src/TemplateSmoke/Secco.TemplateSmoke.Api/Secco.TemplateSmoke.Api.csproj

dotnet ef migrations add Initial \
  --project src/TemplateSmoke/Secco.TemplateSmoke.Migrations.SqlServer/Secco.TemplateSmoke.Migrations.SqlServer.csproj \
  --output-dir Migrations
dotnet ef migrations add Initial \
  --project src/TemplateSmoke/Secco.TemplateSmoke.Migrations.Postgres/Secco.TemplateSmoke.Migrations.Postgres.csproj \
  --output-dir Migrations

SECCO_UPDATE_OPENAPI=true dotnet test \
  src/TemplateSmoke/tests/Secco.TemplateSmoke.Tests/Secco.TemplateSmoke.Tests.csproj --configuration Release

dotnet build src/TemplateSmoke/Secco.TemplateSmoke.Client/Secco.TemplateSmoke.Client.csproj --configuration Release
```

**Esperado: 14 testes, 0 falhas**, e o client compilando a partir do contrato gerado.

Limpeza obrigatória — o produto gerado **não** pode ser commitado:

```bash
rm -rf src/TemplateSmoke
dotnet new uninstall ./templates/secco-service
git status --short          # tem que sair vazio
```

> **Por que `SECCO_UPDATE_OPENAPI=true` aqui?** O `openapi.json` versionado no template é um **esqueleto proposital** (`"paths": { }`), não um snapshot defasado: gerar o contrato é o passo 4 do checklist pós-geração, mesma disciplina das migrations. Sem a variável, o teste de contrato do produto recém-gerado falharia — corretamente.

Nos produtos reais o comportamento é o oposto: o snapshot é versionado e **qualquer divergência falha o CI**. Depois de mudar um contrato, use a task **`contrato: regenerar openapi.json`** e depois **`contrato: regenerar client NSwag`**, e commite os dois no mesmo PR (ADR-0006).

## 6. Teste manual, com a plataforma rodando

### 6.1 Subir a infraestrutura

```bash
docker compose up -d      # SQL Server (1433) + MailHog (1025 SMTP, 8025 UI)
```

`up -d` sobe **só a infra**, sem nenhuma API — é o que o F5 do VS Code espera. Tasks equivalentes: **`infra: subir`**, **`infra: derrubar`**, **`infra: resetar (APAGA os bancos)`**.

### 6.2 Subir as aplicações (F5)

O `.vscode/launch.json` traz uma configuração por produto, e o que muda entre elas é a **auth de desenvolvimento**:

| Configuração | Porta | Auth |
|---|---|---|
| `SecureGate API (standalone, HS256)` | 4001 | chave simétrica local |
| `SecureGate API (self-issued, RS256)` | 4001 | valida os próprios tokens (como produção) |
| `LogStream API (standalone, HS256)` | 4002 | chave simétrica local |
| `LogStream API (federado ao SecureGate)` | 4002 | valida via JWKS do SecureGate |
| `NotificationHub API (standalone, HS256)` | 4003 | chave simétrica local |
| `AdminPortal (Blazor Server)` | 5001 | cookie + OIDC contra o SecureGate |

E dois compounds:

- **`Plataforma: AdminPortal + SecureGate + LogStream`** — o modo federado. **É o único em que o AdminPortal funciona**, porque ele é relying party OIDC e precisa do SecureGate emitindo de verdade.
- **`APIs: todas (standalone)`** — as três APIs isoladas, sem depender uma da outra.

Convenção de portas: API a partir de **4001**, tela a partir de **5001**, HTTP = HTTPS + 100.

### 6.3 Credenciais de desenvolvimento

Só existem em DEV, com guarda dupla (`IsDevelopment()` + `Secco:Seed:Development`, ADR-0019), e o flag já vem ligado no `appsettings.Development.json`. **Nunca** valem fora de DEV.

| Para quê | Identidade | Senha / segredo |
|---|---|---|
| Operador do AdminPortal (cross-tenant) | `operador@secco.local` | `Op3rador@Secco!` |
| Usuário comum de teste | `dev@secco.local` | `Dev@Secco2026` |
| Client confidencial do AdminPortal | `secco-adminportal` | `secco-adminportal-secret-32-chars-min!` |
| Client de máquina (client credentials) | `secco-dev-console` | `secco-dev-console-secret-32-chars-min!` |

Os clients de DEV são declarados em `SecureGate:PlatformClients` no `appsettings.Development.json`
do SecureGate e reconciliados pelo seed de referência (ADR-0037) — o mesmo caminho de uma
instalação real. Client que sair dessa lista é removido na próxima subida.

### 6.4 Roteiro sugerido

1. **APIs de pé.** `https://localhost:4001/health/live` e `/health/ready` em cada API. O `live` responde se o processo está vivo; o `ready` roda os checks de verdade.
2. **Contrato e documentação.** `https://localhost:4002/openapi/v1.json` (contrato cru, público por design) e `https://localhost:4002/scalar` (UI, só em DEV).
3. **Login do operador.** Com o compound `Plataforma`, abra `https://localhost:5001` → redireciona para o SecureGate → entre com `operador@secco.local`. O segundo fator é **obrigatório para o operador** e o seed de DEV não o cadastra: no primeiro login a tela de cadastro TOTP (`/conta/dois-fatores`) aparece antes do code sair — leia o QR com um autenticador e guarde os códigos de recuperação. Depois disso deve voltar autenticado, com o badge do usuário no shell. Num banco de DEV recriado o cadastro some junto.
4. **Tenants.** `/tenants` lista e cria; o drill-in `/tenants/{id}` tem Usuários, Membros, Admins do tenant, Trilha, Perfis, Bancos, Federação e Grupos do diretório. Connection string **nunca** aparece — é write-only por design (ADR-0020). Usuário novo recebe convite por e-mail (ADR-0033), visível no MailHog.
5. **Logs cross-tenant.** `/tenants/{id}/logs` faz busca paginada no LogStream on-behalf-of o operador. É o caminho que a ADR-0024 desenhou: token sem `tenant_id`, tenant escolhido por requisição no header.
6. **Revogação de permissão.** Tire uma permissão do role em `/tenants/{id}` e repita a operação correspondente: com o **mesmo token ainda válido**, ela deve passar a falhar em até um TTL de cache. É a razão de ser da ADR-0021. O mesmo vale para a sessão (ADR-0032): desative a conta ou retire um perfil dela na página da pessoa, e o token em uso deixa de valer em até um TTL de cache.
7. **E-mail do NotificationHub.** `POST /api/v1/notifications` com `channels: ["email"]`; o e-mail aparece na UI do MailHog em `http://localhost:8025`. Com `["in_app"]`, confira em `GET /api/v1/in-app-notifications`.
8. **Isolamento entre tenants.** Repita uma consulta trocando o `X-Tenant-Id`: o dado de um tenant nunca deve aparecer no outro (ADR-0005).

### 6.5 Segundo provider (PostgreSQL)

```bash
docker compose --profile postgres up -d      # Postgres em 5432
```

A paridade já é coberta pelos testes automatizados (`PostgresParityTests` no LogStream e no NotificationHub). Para exercitar à mão, aponte `LogStream:Database:Provider` para `PostgreSql` e use uma connection string de Postgres no catálogo do tenant.

### 6.6 Tudo em container

```bash
docker compose --profile logstream up -d --build        # um produto
docker compose --profile all up -d --build              # os três, com um SQL Server só
```

APIs em container respondem em **HTTP**: 4101 (SecureGate), 4102 (LogStream), 4103 (NotificationHub). Tasks: **`stack: subir um produto em container`** e **`stack: subir tudo em container`**.

Cada produto tem um serviço **`*-migrate`** (`securegate-migrate`, `logstream-migrate` e `notificationhub-migrate`). Ele usa a mesma imagem da API, roda `migrate`, que aplica migrations e o seed de referência, e sai. A API só sobe se ele terminar com `0`, que é o mesmo caminho de um deploy real (ADR-0038). Em `docker compose ps -a`, o esperado é `Exited (0)`. Se a API não subir, o motivo está em `docker compose logs <produto>-migrate`.

## 7. Quando algo falha

| Sintoma | Causa provável |
|---|---|
| Muitos testes falham em ~1 ms com erro de `Docker.DotNet` | Docker saturado — seção 4 |
| `Cannot connect to Docker daemon` | Docker Desktop não está rodando |
| Teste de contrato falha citando o snapshot | Contrato mudou e o `openapi.json` não foi regenerado (ADR-0006) |
| Login do AdminPortal não volta | Está rodando o SecureGate em modo *standalone*; use o compound `Plataforma` |
| Porta ocupada ao subir | Uma stack de container já está de pé — `docker compose down` |
| `NU1015` em toda a solution | `Directory.Packages.props` inválido (XML quebrado); o CPM cai inteiro |

Para começar do zero: **`infra: resetar (APAGA os bancos)`**, ou `docker compose down -v`.
