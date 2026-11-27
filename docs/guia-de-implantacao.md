# Guia de implantação da Secco Platform

> Caminho coberto: **Docker/docker-compose em host único**, atrás de um proxy reverso com TLS,
> com o banco de dados **fora** do host (ou num servidor que o seu DBA administra). Kubernetes,
> várias réplicas e nuvem gerenciada não estão cobertos.

Este guia é para quem vai **rodar a própria instalação** de SecureGate, LogStream, NotificationHub
e AdminPortal, a partir das imagens publicadas no GitHub Container Registry. Você não precisa
compilar este repositório nem conhecer a estrutura dele. Precisa de um host com Docker e Compose v2,
de um SQL Server, de um servidor SMTP e de quatro nomes de DNS. A decisão de distribuir por imagem
está na [ADR-0062](adr/secco-platform-adrs.md).

O exemplo completo está em [`deploy/`](../deploy). O CI sobe esse **mesmo** `docker-compose.yml`, em
Production, antes de cada release e a cada mudança de serviço (`scripts/deploy-smoke.sh`), trocando só
o banco por um container e o SMTP por um falso.

## Limitações conhecidas — leia antes de começar

1. **Homologação também roda com `ASPNETCORE_ENVIRONMENT=Production`.** Várias proteções dependem
   desse valor (cookie `Secure`, metadata OIDC só por HTTPS, chave obrigatória do catálogo). A
   diferença entre homologação e produção fica só na configuração: outras URLs, outro banco, outro
   certificado. `Development` nunca vai a um host exposto: a imagem nem traz as configurações de
   desenvolvimento.
2. **Recriar o container do AdminPortal desconecta quem estiver nele.** O cofre de tokens do console
   é em memória (ADR-0035/0046). As chaves do cookie sobrevivem pelo volume, mas a sessão não. O
   operador volta pelo login do SecureGate, cuja sessão sobrevive. Tokens e sessões do SecureGate
   não são afetados por recriação.
3. **Os bancos de tenant migram no primeiro uso.** O `migrate` do LogStream e do NotificationHub
   migra os tenants que já existem. Um tenant criado depois do deploy tem o banco migrado na primeira
   vez que o serviço o abre (ADR-0038). Se esse banco estiver fora do ar, só esse tenant recebe `503`.
4. **O NotificationHub não lê tenants nem permissões do SecureGate.** O LogStream lê os dois do
   SecureGate. O NotificationHub não, e há duas consequências:
   - os bancos de tenant dele são configurados por variável
     (`Secco__Tenancy__Tenants__{id}__ConnectionString`, uma por tenant);
   - as permissões de cada perfil também são configuradas no próprio serviço
     (`Secco__Authorization__Roles__{perfil}__Permissions__{n}`, ex.: `notifications:write`).

   Sem elas, nenhum chamador tem permissão no NotificationHub, que responde `403`. Do SecureGate
   ele só usa o token, para autenticar, e a publicação do catálogo de permissões.
5. **A checagem do schema do Hangfire é só de existência.** O NotificationHub recusa subir sem o
   schema da fila, mas não percebe um schema mais antigo que o da versão nova. Rode o `migrate` a cada
   atualização, como manda a seção "Atualização".
6. **Certificado em base64 aparece em `docker inspect`.** Prefira entregar o certificado de assinatura
   como arquivo (`secrets:` do Compose), como no exemplo.
7. **Trocar o certificado de assinatura desconecta todo mundo.** O mesmo certificado assina e cifra os
   tokens. Com ele trocado, deixam de valer:
   - os access tokens (até 5 minutos);
   - os refresh tokens, que são cifrados com ele (o banco guarda só os metadados);
   - os codes em andamento.

   Toda pessoa refaz o login, e todo client de máquina pede um token novo. Não há rotação sem queda.

## Imagens e versões

As imagens são **públicas**. O `docker pull` não precisa de login:

| Imagem | Serviço |
|---|---|
| `ghcr.io/rafsecco/secco-securegate` | SecureGate: identidade (OIDC), tenants, catálogo de bancos |
| `ghcr.io/rafsecco/secco-logstream` | LogStream: logs, processos e trilha de auditoria |
| `ghcr.io/rafsecco/secco-notificationhub` | NotificationHub: e-mail, inbox in-app e canais externos (com o Hangfire no mesmo processo) |
| `ghcr.io/rafsecco/secco-adminportal` | AdminPortal: console de operação da instalação |

Tags:

- **`X.Y.Z`**: a que o seu deploy usa. Fixa, nunca muda de conteúdo.
- **`X.Y`**: aponta para a última publicação daquela linha (normalmente o patch mais recente). Use só
  se aceitar receber correção sem trocar a tag.
- **Não existe `latest`.** Nada neste guia depende dele.

As quatro imagens de uma versão saem do mesmo commit, com o mesmo número. A tabela diz qual versão de cada client NSwag corresponde a cada versão da plataforma. Use-a para escolher os pacotes de quem consome os serviços, como o `secco-intranet`:

| Plataforma (imagens) | `Secco.SecureGate.Client` | `Secco.LogStream.Client` | `Secco.NotificationHub.Client` |
|---|---|---|---|
| 0.1.0 | 0.26.0 | 0.5.0 | 0.6.0 |

A release só sai se o contrato de cada serviço for idêntico ao do client publicado (o workflow
recusa), então a correspondência é exata.

Cada imagem:

- usa a base `mcr.microsoft.com/dotnet/aspnet:10.0` (glibc, não Alpine), para `linux/amd64` e
  `linux/arm64`;
- roda como o usuário não-root `app`, **uid 1654**;
- serve HTTP na porta **8080**; o TLS é do proxy;
- é configurada **só por variável de ambiente** (`__` no nome vira `:` na chave);
- tem `HEALTHCHECK` próprio, pelo verbo `healthcheck` do binário, sem `curl` nem `wget`;
- sai com SBOM, provenance e labels OCI. Para conferir:
  `docker buildx imagetools inspect ghcr.io/rafsecco/secco-securegate:0.1.0`.

## Topologia

```text
Internet
   │ HTTPS 443 (e 80 para o ACME)
   ▼
proxy (Caddy)  ──── rede interna "secco" (subnet fixa, ex.: 172.30.0.0/24) ────┐
   │  login.exemplo.com.br        → securegate:8080                            │
   │  logs.exemplo.com.br         → logstream:8080                             │
   │  notificacoes.exemplo.com.br → notificationhub:8080                       │
   │  admin.exemplo.com.br        → adminportal:8080                           │
   │                                                                           │
   └── SQL Server externo (bancos de plataforma e de tenant) ◄─────────────────┘
       SMTP externo
```

- **Só o proxy publica porta.** Os serviços não têm `ports:`. Eles aceitam `X-Forwarded-For` e
  `X-Forwarded-Proto` **apenas** de endereços da rede interna (`Secco__ReverseProxy__KnownNetworks`).
  Expor a porta 8080 de um serviço deixaria qualquer um forjar o esquema e o IP de origem.
- **Os serviços chamam o SecureGate pelo nome público, por HTTPS.** Em Production isso é exigido.
  No compose, o proxy tem os nomes públicos como **aliases de rede**: dentro da rede, o nome resolve
  para o proxy, sem depender de o host conseguir alcançar o próprio IP público (*hairpin NAT*).
- **`AllowedHosts`** de cada serviço é o nome público dele mais `localhost` (o `localhost` é o que o
  healthcheck usa). Requisição com outro `Host` recebe `400`.

## Banco de dados

O SQL Server é externo. Num banco gerenciado, quem cria os bancos e os logins é o seu DBA, com o
script [`deploy/sql/sqlserver-plataforma.sql`](../deploy/sql/sqlserver-plataforma.sql):

```bash
sqlcmd -S sql.exemplo.com.br -U <admin> -C -b -i deploy/sql/sqlserver-plataforma.sql \
  -v SECUREGATE_DB_MIGRATOR_PASSWORD="..." SECUREGATE_DB_APP_PASSWORD="..." \
     NOTIFICATIONHUB_JOBS_MIGRATOR_PASSWORD="..." NOTIFICATIONHUB_JOBS_APP_PASSWORD="..."
```

Ele cria dois bancos de **plataforma**, cada um com dois logins:

| Banco | Login | Permissão | Quem usa |
|---|---|---|---|
| `secco_securegate` | `secco_securegate_migrator` | `db_owner` no banco | só o `migrate` do SecureGate |
| `secco_securegate` | `secco_securegate_app` | `db_datareader` + `db_datawriter` | o SecureGate em execução (e o seed do `migrate`) |
| `secco_notificationhub_jobs` | `secco_nh_jobs_migrator` | `db_owner` no banco | só o `migrate` do NotificationHub (schema do Hangfire) |
| `secco_notificationhub_jobs` | `secco_nh_jobs_app` | `db_datareader` + `db_datawriter` + `EXECUTE` | o NotificationHub em execução (fila do Hangfire) |

- **No SecureGate, use as duas credenciais, e diferentes** (ADR-0041). Sem a de migração, o `migrate` usa a de runtime, que não tem DDL; dando posse à de runtime, o SecureGate recusa subir. O banco tem Row-Level Security, e o dono do banco conseguiria desligá-lo. Fora de Development, o SecureGate
  **recusa subir** com credencial de runtime dona do banco.
- **Nenhum login recebe permissão no servidor**, e a aplicação nunca usa `sa`.
- **Senhas sem `;` nem aspas**, porque entram numa connection string e entre aspas simples no `CREATE LOGIN`. Um prefixo com maiúscula sobre hex atende também a política de complexidade do SQL Server em Windows (`CHECK_POLICY = ON` pede 3 categorias): `echo "Sg$(openssl rand -hex 24)"`.
- **O LogStream não tem banco de plataforma.** Os bancos de **tenant** de LogStream e NotificationHub
  vêm do provisionamento do SecureGate (ADR-0028, pelo AdminPortal ou pela API) ou do seu DBA, um
  banco e um login por tenant, com `db_owner` só nele.
- **PostgreSQL** é suportado pelos serviços (`*__Database__Provider=PostgreSql`, ADR-0018), mas o
  script e o exemplo deste guia são de SQL Server. A fila do Hangfire é só SQL Server.

## Certificado de assinatura do SecureGate

O SecureGate assina e cifra os tokens com um certificado PKCS#12 seu. Ele é **obrigatório** em todo
ambiente que não seja Development nem Testing, e é o que faz os tokens continuarem válidos quando o
container é recriado.

```bash
mkdir -p deploy/secrets
openssl req -x509 -newkey rsa:2048 -nodes -days 730 -subj "/CN=SecureGate signing" \
  -keyout signing.key -out signing.crt
openssl pkcs12 -export -inkey signing.key -in signing.crt \
  -out deploy/secrets/securegate-signing.pfx -passout "pass:<senha>"
rm signing.key signing.crt
chmod 644 deploy/secrets/securegate-signing.pfx   # o container lê como uid 1654
```

Duas formas de entregar, **nunca as duas**:

- **Arquivo** (recomendado, é o do exemplo): `SecureGate__Signing__CertificatePath` aponta para o
  arquivo montado como `secrets:` do Compose (`/run/secrets/securegate-signing`).
- **Base64**: `SecureGate__Signing__CertificateBase64` com o conteúdo do `.pfx`
  (`base64 -w0 securegate-signing.pfx`; quebras de linha também são aceitas). Serve para quem só pode
  usar variável, mas o valor aparece em `docker inspect`.

`SecureGate__Signing__CertificatePassword` é a senha do `.pfx` (opcional). Guarde o `.pfx` e a senha
fora do host: perder o certificado invalida todos os tokens emitidos.

## Variáveis de ambiente

O exemplo monta todas elas a partir do [`deploy/.env.example`](../deploy/.env.example). Copie para
`deploy/.env` e preencha. Segredos em hex (`openssl rand -hex 32`); valor com `#`, `$`, `;` ou espaço
vai entre aspas simples.

### Comuns aos quatro serviços

| Variável | Obrigatória | Descrição |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Sim | `Production`, também na homologação. |
| `AllowedHosts` | Sim | Nome público do serviço e `localhost`, separados por `;`. |
| `Secco__ReverseProxy__Enabled` | Sim, atrás de proxy | `true` para aceitar `X-Forwarded-For`/`X-Forwarded-Proto`. Padrão `false`. |
| `Secco__ReverseProxy__KnownNetworks__0` (e `__1`...) | Sim, com o proxy ligado | CIDR da rede de onde o proxy conecta (ex.: `172.30.0.0/24`). Sem ela, ou com CIDR inválido, o serviço não sobe. |
| `ASPNETCORE_HTTP_PORTS` | Não (a imagem define `8080`) | Porta HTTP interna. O healthcheck a segue (e segue `ASPNETCORE_URLS`, se definida). |
| `Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command` | Recomendada | `Warning`. Sem ela, cada comando SQL vai ao log. |

### SecureGate

| Variável | Obrigatória | Descrição |
|---|---|---|
| `SecureGate__Database__ConnectionString` | Sim | Credencial de **runtime**: `secco_securegate_app` (leitura e escrita, nunca dona). |
| `SecureGate__Database__MigrationConnectionString` | Na prática sim, no `migrate` | Credencial **dona**: `secco_securegate_migrator`. Só no serviço `securegate-migrate`. Ausente, o `migrate` usa a de runtime, que não tem DDL. |
| `SecureGate__Database__Provider` | Não (padrão `SqlServer`) | `SqlServer` ou `PostgreSql`. |
| `SecureGate__PublicBaseUrl` | Sim | URL pública do SecureGate (`https://login.exemplo.com.br`). Base dos links de convite e recuperação. Nunca vem do `Host` da requisição. |
| `SecureGate__Catalog__EncryptionKey` | Sim | Chave que cifra as connection strings de tenant no banco (ADR-0025): base64 de 32 bytes (`openssl rand -base64 32`). Entra no backup com o mesmo peso do banco. |
| `SecureGate__Signing__CertificatePath` ou `SecureGate__Signing__CertificateBase64` | Sim, uma das duas | Certificado de assinatura (seção anterior). |
| `SecureGate__Signing__CertificatePassword` | Não | Senha do `.pfx`. |
| `Secco__Authentication__Authority` | Sim | A própria URL pública (`https://login.exemplo.com.br`): o SecureGate valida os tokens das próprias APIs de gestão. |
| `Secco__Authentication__Audience` | Sim | `secco-securegate`. |
| `SecureGate__Email__Host` / `Port` / `UseStartTls` | Sim | SMTP do convite e da recuperação de senha (ADR-0033). Fora de Development o SecureGate não sobe sem e-mail. |
| `SecureGate__Email__Username` / `Password` | Se o SMTP autentica | Credenciais do SMTP. Vazias, não autentica. |
| `SecureGate__Email__FromAddress` / `FromName` | Sim / Não | Remetente. |
| `SecureGate__PlatformClients__{n}__*` | Sim | Clients de plataforma, um índice por client (tabela abaixo). A lista é a fonte da verdade: o `migrate` cria, atualiza e **remove** o client que sair dela. |

Clients de plataforma do exemplo (`ClientSecret` com ao menos 32 caracteres):

| `ClientId` | `Type` | `Scopes` | Para quê |
|---|---|---|---|
| `logstream-service` | `ClientCredentials` | `catalog:logstream`, `authorization:read`, `permissions:logstream` | o LogStream lê o catálogo de tenants e as permissões e publica o catálogo de permissões dele |
| `notificationhub-service` | `ClientCredentials` | `permissions:notificationhub` | o NotificationHub publica o catálogo de permissões no `migrate` |
| `adminportal` | `AuthorizationCode` | `email`, `profile`, `roles`, `securegate:admin`, `logstream` | login do operador no console. `RedirectUris__0` = `https://admin.exemplo.com.br/signin-oidc`, `PostLogoutRedirectUris__0` = `https://admin.exemplo.com.br/signout-callback-oidc` |
| `adminportal-sessions` | `ClientCredentials` | `authorization:read` | o console confere se a sessão do operador foi revogada (ADR-0032) |

Os clients de quem consome a plataforma, como a Intranet, entram nesta mesma lista, com os escopos que o guia de deploy dela pede.

Opcionais mais comuns: `SecureGate__EntraId__*` (login federado, ADR-0026), `SecureGate__Audit__*`
(trilha da elevação e da gestão por máquina), `SecureGate__Provisioning__*` (criar banco de tenant
pelo SecureGate, ADR-0028), `SecureGate__InstallationOperators__Accounts__{n}` (operador designado,
ADR-0049). Cada um está descrito no [README do SecureGate](../src/SecureGate/README.md).

### LogStream

| Variável | Obrigatória | Descrição |
|---|---|---|
| `Secco__Authentication__Authority` | Sim | URL pública do SecureGate. |
| `Secco__Authentication__Audience` | Sim | `secco-logstream`. |
| `Secco__SecureGate__BaseUrl` | Sim | URL pública do SecureGate. Liga o catálogo de tenants e a resolução de permissões remotos. |
| `Secco__SecureGate__ClientId` / `ClientSecret` | Sim | O client `logstream-service`. É a **credencial** do LogStream: com ela ele lê, no catálogo do SecureGate, a connection string de cada tenant. Não há credencial de banco própria. |
| `Secco__SecureGate__Product` | Sim | `logstream`. |
| `LogStream__Database__Provider` | Não (padrão `SqlServer`) | Engine dos bancos de tenant. |
| `LogStream__Retention__*` | Não | Retenção opt-in por classe de dado. A trilha de auditoria não expira por padrão. Ver o README do LogStream. |

### NotificationHub

| Variável | Obrigatória | Descrição |
|---|---|---|
| `NotificationHub__BackgroundJobs__ConnectionString` | Sim | Credencial de **runtime** da fila do Hangfire: `secco_nh_jobs_app`. |
| `NotificationHub__BackgroundJobs__MigrationConnectionString` | Recomendada, no `migrate` | Credencial **dona** (`secco_nh_jobs_migrator`), usada só para instalar o schema da fila. Sem ela, o `migrate` usa a de runtime, que então precisaria de DDL. |
| `Secco__Tenancy__Tenants__{tenantId}__ConnectionString` | Uma por tenant | Banco de tenant do NotificationHub (ver limitação 4). |
| `Secco__Authentication__Authority` / `Audience` | Sim | URL pública do SecureGate / `secco-notificationhub`. |
| `Secco__SecureGate__BaseUrl` / `ClientId` / `ClientSecret` | Sim | O client `notificationhub-service`, para publicar o catálogo de permissões no `migrate`. |
| `Secco__Authorization__Roles__{perfil}__Permissions__{n}` | Sim, para ter acesso | Permissões de cada perfil **neste serviço** (ex.: `notifications:read`, `notifications:write`, `in-app-notifications:read`). O NotificationHub não as resolve pelo SecureGate (limitação 4). |
| `NotificationHub__Email__Host` / `Port` / `UseStartTls` / `Username` / `Password` / `FromAddress` / `FromName` | Sim | SMTP do canal de e-mail (mesmo formato do SecureGate). `Provider=SendGrid` com `ApiKey` também é aceito. |
| `NotificationHub__BackgroundJobs__VerifySchemaOnStartup` | Não (padrão `true`) | Existe para testes. Não desligue: é o que impede subir sem o schema da fila. |

### AdminPortal

| Variável | Obrigatória | Descrição |
|---|---|---|
| `Secco__SecureGate__Authority` / `ApiBaseUrl` | Sim | URL pública do SecureGate (login do operador e API de gestão). |
| `Secco__SecureGate__ClientId` / `ClientSecret` | Sim | O client `adminportal`. |
| `Secco__SessionValidation__BaseUrl` / `ClientId` / `ClientSecret` | Sim (não impede a subida) | URL do SecureGate e o client `adminportal-sessions`. Sem a seção o console sobe, mas não percebe sessão de operador revogada (ADR-0032). |
| `Secco__LogStream__BaseUrl` | Sim | URL pública do LogStream (leitura de logs e exportação da trilha). |
| `AdminPortal__DataProtection__KeysPath` | Não (a imagem define `/var/lib/secco-adminportal/keys`) | Pasta das chaves do cookie e do antiforgery. Monte-a (próxima seção). |

## Volumes

| Serviço | Caminho no container | Dono | O que guarda |
|---|---|---|---|
| AdminPortal | `/var/lib/secco-adminportal/keys` (`VOLUME`) | uid **1654**, modo **700** | chaves do DataProtection (cookie de sessão e antiforgery), em claro |
| Proxy (Caddy) | `/data` | o do Caddy | certificados TLS emitidos pelo ACME |

O SecureGate **não tem volume**: as chaves do DataProtection dele ficam no banco, e o certificado de
assinatura é seu. LogStream e NotificationHub também não guardam estado no disco.

Crie as pastas com o dono certo **antes** do primeiro `up`:

```bash
cd deploy
mkdir -p data/adminportal-keys data/caddy
sudo chown 1654:1654 data/adminportal-keys && sudo chmod 700 data/adminportal-keys
```

A pasta das chaves do AdminPortal é segredo: quem a tem forja o cookie do console. Ela entra no
backup com a mesma proteção da `.env`.

## `docker-compose` de host único

O arquivo completo é o [`deploy/docker-compose.yml`](../deploy/docker-compose.yml), com o
[`Caddyfile`](../deploy/Caddyfile). O essencial:

```yaml
services:
  proxy:
    image: caddy:2.10@sha256:...
    ports: ["443:443", "80:80"]
    networks:
      secco:
        aliases: [login.exemplo.com.br, logs.exemplo.com.br, notificacoes.exemplo.com.br, admin.exemplo.com.br]

  securegate-migrate:
    image: ghcr.io/rafsecco/secco-securegate:${SECCO_VERSION}
    command: ["migrate"]
    restart: "no"
    environment:
      <<: *securegate-env
      SecureGate__Database__MigrationConnectionString: "...secco_securegate_migrator..."
    secrets: [securegate-signing]

  securegate:
    image: ghcr.io/rafsecco/secco-securegate:${SECCO_VERSION}
    environment: *securegate-env        # sem a credencial de migração
    secrets: [securegate-signing]
    depends_on:
      securegate-migrate: { condition: service_completed_successfully }

  logstream-migrate:                    # publica o catálogo de permissões no SecureGate
    depends_on:
      securegate: { condition: service_healthy }
  # logstream, notificationhub-migrate, notificationhub, adminportal: mesmo padrão

networks:
  secco:
    ipam:
      config: [{ subnet: 172.30.0.0/24 }]
```

## Primeira subida

Do diretório `deploy/`, com o `.env` preenchido, o banco criado pelo DBA, o certificado em
`secrets/` e as pastas de volume com o dono certo:

```bash
docker compose pull

# 1. SecureGate: migrations com a credencial dona e seed de referência (escopos, tenant de
#    plataforma, papel de operador, clients de PlatformClients). Sai 0 ou 1.
docker compose run --rm securegate-migrate

# 2. SecureGate e proxy no ar
docker compose up -d securegate proxy
curl -fsS https://login.exemplo.com.br/health/ready

# 3. LogStream e NotificationHub: migram os tenants que existirem, instalam o schema da fila e
#    publicam o catálogo de permissões no SecureGate (por isso ele precisa estar no ar)
docker compose run --rm logstream-migrate
docker compose run --rm notificationhub-migrate

# 4. O resto
docker compose up -d
docker compose ps        # os quatro serviços "healthy"
```

Um `docker compose up -d` sozinho faz a mesma sequência pelos `depends_on`. Os passos separados
mostram onde parou, quando algo falha.

- **`migrate` saiu `1`.** O log diz qual alvo falhou, sem connection string. O seed de referência não
  roda sobre schema pela metade.
- **Banco inalcançável.** O `migrate` sai `1` em segundos.
- **O SecureGate recusa subir.** Há três motivos possíveis, e o log diz qual:
  - migration pendente;
  - credencial de runtime dona do banco;
  - falta certificado ou e-mail.

## Primeiro operador

Uma instalação nova não tem nenhum operador (`installation-operator`), e sem ele ninguém entra no
AdminPortal. O caminho usa a API do SecureGate com um client **temporário**:

1. Ponha no `.env` um segredo para o client temporário (`BOOTSTRAP_OPERATOR_SECRET`, 32 ou mais
   caracteres). Suba o SecureGate com o override [`deploy/bootstrap-operador.yml`](../deploy/bootstrap-operador.yml),
   que acrescenta o client `bootstrap-operador` com `securegate:admin` ao `securegate` e ao
   `securegate-migrate`. Uma variável solta no `.env` não chega ao container: o `.env` só preenche os
   `${...}` do compose.

   ```bash
   echo "BOOTSTRAP_OPERATOR_SECRET=$(openssl rand -hex 32)" >> .env
   docker compose -f docker-compose.yml -f bootstrap-operador.yml run --rm securegate-migrate
   docker compose -f docker-compose.yml -f bootstrap-operador.yml up -d securegate
   ```

2. Crie a conta no tenant de plataforma (`018f0000-0000-7000-8000-0000000000ff`) e atribua o papel. A
   conta nasce sem senha. O SecureGate manda o convite por e-mail (ADR-0033), e a pessoa define a
   senha e cadastra o segundo fator, que é obrigatório para operador.

   ```bash
   export "$(grep ^BOOTSTRAP_OPERATOR_SECRET= .env)"
   TOKEN=$(curl -fsS https://login.exemplo.com.br/connect/token \
     -d grant_type=client_credentials -d client_id=bootstrap-operador \
     -d client_secret="$BOOTSTRAP_OPERATOR_SECRET" -d scope=securegate:admin | jq -r .access_token)

   USER_ID=$(curl -fsS -X POST https://login.exemplo.com.br/api/v1/tenants/018f0000-0000-7000-8000-0000000000ff/users \
     -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
     -d '{"email":"operador@exemplo.com.br","displayName":"Operador"}' | jq -r .id)

   curl -fsS -X POST "https://login.exemplo.com.br/api/v1/tenants/018f0000-0000-7000-8000-0000000000ff/users/$USER_ID/roles/installation-operator" \
     -H "Authorization: Bearer $TOKEN"
   ```

3. **Tire o client: rode o `migrate` e suba o SecureGate sem o override**, e apague a linha do
   `.env`. O seed de referência remove o client que saiu da lista (ADR-0037). Um segredo com
   `securegate:admin` esquecido alcança a instalação inteira.

   ```bash
   docker compose run --rm securegate-migrate
   docker compose up -d securegate
   sed -i '/^BOOTSTRAP_OPERATOR_SECRET=/d' .env
   ```

Daí em diante, os demais operadores e tenants se criam pelo AdminPortal.

## Health checks

- `GET /health/live`: o processo responde. Use como liveness.
- `GET /health/ready`: prontidão. É o que o `HEALTHCHECK` da imagem consulta (verbo `healthcheck`,
  `docker run --rm <imagem> healthcheck`). Hoje não consulta banco nem SecureGate.

Os dois são anônimos. O healthcheck chama `http://127.0.0.1:8080/health/ready` com `Host: localhost`, sem seguir redirecionamento. Por isso o `AllowedHosts` precisa incluir `localhost`.

## Atualização e rollback

Atualizar é trocar a tag:

```bash
# .env: SECCO_VERSION=0.1.0 → 0.2.0  (leia antes o CHANGELOG da versão)
docker compose pull
docker compose run --rm securegate-migrate
docker compose up -d securegate
docker compose run --rm logstream-migrate
docker compose run --rm notificationhub-migrate
docker compose up -d
```

O `migrate` é obrigatório a cada versão, mesmo sem migration nova: ele também reconcilia os clients
de plataforma e o catálogo de permissões, e atualiza o schema da fila do Hangfire.

Rollback é voltar a tag e rodar `up -d`. **Migration já aplicada não é desfeita**: uma versão anterior
só roda sobre o schema novo se o CHANGELOG da versão nova disser que ele é compatível. Fora disso, o
rollback é restaurar o backup do banco feito antes do `migrate`. Faça esse backup antes de toda
atualização.

## Backup

Entram no backup, com o mesmo nível de proteção:

- os bancos (`secco_securegate`, `secco_notificationhub_jobs` e os de tenant);
- o `deploy/.env` (senhas, chave do catálogo, segredos dos clients);
- o certificado de assinatura e a senha dele;
- a pasta `deploy/data/adminportal-keys`.

A chave do catálogo sem o banco não abre nada. O banco sem a chave não abre as connection strings de
tenant (ADR-0025).

## Checklist antes de ir ao ar

- [ ] `SECCO_VERSION` numa tag `X.Y.Z` fixa.
- [ ] `ASPNETCORE_ENVIRONMENT=Production` em todos os serviços, também na homologação.
- [ ] Só o proxy publica porta; `KnownNetworks` igual à subnet da rede do compose.
- [ ] `AllowedHosts` de cada serviço com o nome público dele e `localhost`.
- [ ] Credencial de runtime do SecureGate **sem** posse do banco; a dona só no `securegate-migrate`.
- [ ] Certificado de assinatura e chave do catálogo gerados e guardados fora do host.
- [ ] Pasta das chaves do AdminPortal com dono 1654 e modo 700.
- [ ] `migrate` dos três serviços saiu `0`; os quatro serviços `healthy`.
- [ ] Primeiro operador criado, e o client temporário de `bootstrap` **removido**.
- [ ] Backup dos bancos, do `.env`, do certificado e da pasta de chaves testado.
