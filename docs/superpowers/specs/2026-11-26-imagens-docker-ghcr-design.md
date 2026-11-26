# Imagens Docker versionadas no GHCR — design

**Data:** 2026-11-26
**ADR:** ADR-0062 (a escrever junto com a implementação; a ADR-0060 está reservada no branch `feat/issue-6-acessar-como`)
**Origem:** pedido do adotante `secco-intranet`, cuja esteira de deploy hoje precisaria compilar os serviços a partir do monorepo.

## Intenção

Quem adota a plataforma implanta SecureGate, LogStream, NotificationHub e AdminPortal **baixando uma imagem pronta com tag fixa**, sem compilar o monorepo. A esteira do adotante faz `pull`, roda `migrate` e sobe. A mesma imagem serve homologação e produção. Hoje, qualquer mudança de pasta ou de Dockerfile aqui quebraria o deploy de lá, porque o adotante dependeria da estrutura interna do repositório.

## Objetivos

- Um push de tag `vX.Y.Z` publica as imagens no GHCR com as tags `X.Y.Z` e `X.Y`, labels OCI, SBOM e provenance, para `linux/amd64` e `linux/arm64`.
- Uma imagem só é publicada depois de passar pela varredura de vulnerabilidades (reprova em CRITICAL) e por um smoke que sobe o compose do guia.
- Cada serviço sobe só com variáveis de ambiente, como não-root, responde `200` em `/health/ready` e tem `HEALTHCHECK` sem `curl`.
- Recriar um container não invalida os tokens emitidos nem as sessões do SecureGate.
- Existe um guia de implantação da plataforma, com um compose de host único que sobe tudo do zero. É esse mesmo arquivo que o CI exercita.
- A primeira release (`v0.1.0`) é publicada ao final.

## Não-objetivos

- Kubernetes, múltiplos hosts e múltiplas réplicas. O guia cobre host único, como o da intranet.
- Persistir as sessões do AdminPortal entre recriações (decisão abaixo).
- Imagem Alpine ou *distroless*.
- Mirror das imagens em outro registro.
- Mudar o versionamento dos pacotes NuGet (MinVer por prefixo continua igual).

## Anti-objetivos (falha mesmo que funcione)

- Uma imagem publicada com segredo conhecido dentro (chave HS256 de dev, client secret de dev).
- Uma imagem publicada antes de ser varrida: corrigir depois vira "apagar versão pública".
- Uma documentação que dependa de `latest`.
- Um guia cujo compose nunca foi executado.
- Um serviço que, atrás do proxy do guia, recuse o próprio tráfego, por exemplo um OIDC que exige HTTPS e enxerga HTTP.

## Decisões tomadas com o Rafael

| Decisão | Escolha |
|---|---|
| Visibilidade no GHCR | **Públicas.** O repositório já é público e, sem o `appsettings.Development.json`, a imagem não carrega segredo. O pull dispensa PAT. Um pacote que vira público não volta a ser privado. |
| Versionamento | **Uma versão da plataforma para todas as imagens.** `v0.1.0` gera `secco-*:0.1.0` e `secco-*:0.1`. Uma tabela mapeia cada versão da plataforma às versões dos `*.Client`. |
| Sessões do AdminPortal | **Aceitar e documentar.** O cofre fica em memória (ADR-0035/0046), e recriar o container leva o operador ao re-login. A sessão do SecureGate sobrevive. |
| Ordem no workflow | **Varrer e testar antes do push** (abordagem A abaixo). |
| Healthcheck | **Verbo `healthcheck` no próprio binário** (abordagem A abaixo). |

## Abordagens consideradas

**Workflow:**
- **A. Varredura e smoke antes do push (escolhida).** Cada imagem é construída em amd64 e carregada no runner, passa pelo Trivy e pelo smoke do compose do guia. Só então vem o build multi-arch com push, reaproveitando o cache do BuildKit. Uma imagem vulnerável ou quebrada nunca chega ao registro.
- **B. Push e varredura depois.** O workflow fica mais simples, mas a imagem reprovada já está pública com a tag.

**Healthcheck sem `curl`/`wget`:**
- **A. Verbo `healthcheck` no binário, vindo do SDK (escolhida).** O verbo faz um GET local e sai 0 ou 1 antes de montar o host. Não há dependência nova, e o template herda o padrão.
- **B. Executável de probe estático copiado para a imagem.** É mais leve por execução, mas é mais um artefato para manter e auditar contra CVE.
- *Descartada:* o `bash -c '</dev/tcp/...'`, que a intranet usa, só prova que a porta abriu e não lê o status HTTP.

**Versão das imagens:** uma versão da plataforma (escolhida) × uma tag por produto alinhada ao client. A segunda dá correspondência direta com o client, mas exige quatro tags por release, esbarra no limite de três tags por push do GitHub e faz o compose do adotante misturar versões.

## Achados que moldam o design

1. **O feed privado não entra no build.** Todo `Secco.*` é `ProjectReference`, nenhum csproj de `src/` tem `PackageReference` para `Secco.*`, e o restore só usa o nuget.org. O workflow **não passa credencial nenhuma** ao build. Se um dia um `Secco.*` virar `PackageReference`, o build falha de forma visível, e aí entra `--mount=type=secret` (registrado na ADR).
2. **O Hangfire roda dentro do processo do NotificationHub** (`AddNotificationHubBackgroundJobs`). Não há imagem separada para ele.
3. **O AdminPortal é implantável** (ADR-0051: *o* console da instalação) e não tem Dockerfile. Ele ganha um.
4. **O `appsettings.Development.json` entra na imagem** com a `DevelopmentSigningKey` HS256 e client secrets conhecidos. Numa imagem pública, subir com `ASPNETCORE_ENVIRONMENT=Development` significa aceitar token forjado. A imagem de release passa a sair sem ele.
5. **Nenhum produto trata `X-Forwarded-*`.** Atrás do proxy TLS do guia, o OpenIddict em Production exige HTTPS (`DisableTransportSecurityRequirement` só fora de Production) e veria HTTP, recusando todo o OIDC. O AdminPortal montaria a `redirect_uri` com `http://`. Sem corrigir isso, a topologia do guia não funciona. Esta é a maior adição de escopo.
6. **Chaves:**
   - O SecureGate já guarda as chaves do DataProtection no banco (ADR-0033).
   - A chave de assinatura OIDC vem de certificado (`SecureGate:Signing:CertificatePath`), mas **só em Production**. Em qualquer outro ambiente (Staging, por exemplo) o OpenIddict gera um certificado de desenvolvimento no `$HOME` do container, que se perde na recriação.
   - O AdminPortal usa DataProtection no caminho padrão do framework (`$HOME/.aspnet`).
   - LogStream e NotificationHub não guardam estado local que precise sobreviver: Hangfire e tenants ficam no banco, e o temporário de exportação é descartável.
7. **`migrate` × "recusa subir com migration pendente":**
   - LogStream e NotificationHub não têm banco de plataforma EF. Os tenants migram no primeiro uso (ADR-0038), e essa ADR recusou de propósito conferir todos os tenants na subida.
   - Fora de DEV os dois já não migram na subida, e isso fica como está.
   - A exceção é o **schema do Hangfire**, que o próprio Hangfire cria e atualiza quando o NotificationHub sobe (`PrepareSchemaIfNecessary`, padrão `true`). Isso é migrar na subida, e passa para o `migrate`.
8. **O primeiro operador.** Uma instalação nova não tem caminho documentado para criar o primeiro `installation-operator` (só o seed de DEV cria um). O guia vai documentar o caminho pela API existente (seção 8 do design).
9. **Os contratos de hoje batem com os clients publicados.** `openapi.json` no `HEAD` é igual ao da última tag de `securegate-client` (0.26.0), `logstream-client` (0.5.0) e `notificationhub-client` (0.6.0).

## Design

### 1. Imagens e nomes

| Imagem | Processo | Dockerfile |
|---|---|---|
| `ghcr.io/rafsecco/secco-securegate` | `Secco.SecureGate.Api` | `src/SecureGate/Secco.SecureGate.Api/Dockerfile` (existente, revisado) |
| `ghcr.io/rafsecco/secco-logstream` | `Secco.LogStream.Api` | `src/LogStream/Secco.LogStream.Api/Dockerfile` (existente, revisado) |
| `ghcr.io/rafsecco/secco-notificationhub` | `Secco.NotificationHub.Api` (com Hangfire) | `src/NotificationHub/Secco.NotificationHub.Api/Dockerfile` (existente, revisado) |
| `ghcr.io/rafsecco/secco-adminportal` | `Secco.AdminPortal` | `src/AdminPortal/Secco.AdminPortal/Dockerfile` (**novo**) |

O padrão de nome `secco-<produto>` em minúsculas é o que o compose de dev já usa nas tags locais. O owner sai de `github.repository_owner`, nunca digitado no workflow.

### 2. Dockerfile: o molde comum aos quatro

- O contexto continua sendo a raiz do repositório e os `COPY` continuam por produto. O de dev do compose segue funcionando sem mudança de caminho.
- `# syntax=docker/dockerfile:1` no topo.
- **Estágio de build em `--platform=$BUILDPLATFORM`.** O `dotnet publish` com `UseAppHost=false` e sem RID gera IL portátil, então o mesmo output serve amd64 e arm64. Só a imagem base de runtime muda por `TARGETPLATFORM`, e o arm64 sai sem QEMU no `dotnet publish`.
- `ARG SECCO_VERSION=0.0.0`, passado como `/p:Version=$SECCO_VERSION` (com `/p:MinVerSkip=true` mantido), para a versão do assembly ser a da release.
- `ARG SECCO_INCLUDE_DEV_SETTINGS=false`. Com `false`, o build apaga `appsettings.Development.json` do output antes do estágio final. O `docker-compose.yml` de dev passa `true` em `build.args`, e a release nunca passa. O default seguro é o de release.
- Runtime `mcr.microsoft.com/dotnet/aspnet:10.0` (glibc). `ENV ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`, `USER $APP_UID` (uid **1654**, o usuário `app` da imagem base).
- `HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 CMD ["dotnet", "Secco.X.Api.dll", "healthcheck"]`.
- `ENTRYPOINT ["dotnet", "Secco.X.Api.dll"]`, inalterado, de modo que `docker run --rm <imagem> migrate` continua valendo.
- O Dockerfile não declara labels: quem as escreve é o `docker/metadata-action` no workflow, para as tags e as labels saírem da mesma fonte.
- **AdminPortal:** `ENV AdminPortal__DataProtection__KeysPath=/var/lib/secco-adminportal/keys`, a pasta criada com dono 1654 e modo 0700 no build, e `VOLUME /var/lib/secco-adminportal/keys`. Os outros três não declaram `VOLUME` (achado 6).

### 3. SDK: verbo `healthcheck` (`Secco.SDK.AspNetCore`)

- `SeccoHealthCheckCommand.IsHealthCheck(string[] args)`: verdadeiro quando o primeiro argumento é exatamente `healthcheck`, pelo mesmo critério do `SeccoCommands.IsMigrate`. Ele fica no `Secco.SDK.AspNetCore` porque o AdminPortal não referencia o pacote de EF Core.
- `SeccoHealthCheckCommand.RunAsync()`: faz um GET em `http://127.0.0.1:{porta}/health/ready` com `Host: localhost` e timeout de 5 s. Devolve 0 em `200` e 1 em qualquer outro caso: status diferente, recusa de conexão, timeout ou porta inválida. Sem argumento externo, ele não loga nada além de uma linha no stderr em caso de falha.
- **A porta** sai do primeiro valor de `ASPNETCORE_HTTP_PORTS`, separado por `;`, ou 8080 quando a variável está vazia. O valor é validado como inteiro de 1 a 65535; fora disso, sai 1. É a única entrada, e vem do ambiente do próprio container.
- **Por que `ready` e não `live`.** O compose usa `condition: service_healthy` para ordenar a subida, e o que importa ali é a prontidão. Hoje o `ready` só roda checks registrados, e nenhum produto registra check de dependência, então na prática ele equivale ao `live`. Se um check de banco entrar depois, o healthcheck passa a refleti-lo sem mudar a imagem.
- Em cada `Program.cs`, inclusive o do template, a **primeira** linha passa a ser `if (SeccoHealthCheckCommand.IsHealthCheck(args)) return await SeccoHealthCheckCommand.RunAsync();`, antes de `CreateBuilder`. Assim o probe não monta host, DI nem configuração.
- `AllowedHosts`: com o domínio fixado, o probe seria barrado com 400. Por isso o probe envia `Host: localhost`, e o guia manda incluir `localhost` no `AllowedHosts`.

### 4. SDK: proxy reverso (`Secco.SDK.AspNetCore`)

- A seção `Secco:ReverseProxy` tem `Enabled` (padrão `false`) e `KnownNetworks` (lista de CIDR).
- `AddSeccoForwardedHeaders()` (dentro do `AddSeccoPlatform()` e chamável à parte pelo AdminPortal) configura `ForwardedHeadersOptions`: só `X-Forwarded-For` e `X-Forwarded-Proto`, `ForwardLimit = 1`, `KnownNetworks` substituídas pelas da configuração e `KnownProxies` limpas. O `Host` não é reescrito: o proxy repassa o `Host` original, e o `AllowedHosts` o restringe.
- `UseSeccoForwardedHeaders()` é um no-op quando `Enabled=false` e vira o **primeiro** middleware de cada `Program.cs`, antes dos cabeçalhos de segurança e do `UseSeccoPlatform()`. Ele fica explícito no `Program.cs` porque o SecureGate põe `UseSeccoSecurityHeaders` antes do `UseSeccoPlatform`, e a ordem precisa ser visível.
- **Fail-fast:** com `Enabled=true` e `KnownNetworks` vazia, ou com um CIDR inválido, a aplicação não sobe.
  - *Risco considerado:* confiar em qualquer origem, como faz a intranet, deixa quem alcançar a porta direto forjar o IP do cliente (partição de rate limit, log) e o esquema.
  - *Escolha:* exigir a rede. O compose do guia fixa a subnet da rede interna, o que torna a lista trivial de preencher.
- A ADR-0004 já põe o cross-cutting no SDK; isto é cross-cutting novo, sem decisão estrutural nova além do fail-fast. Entra na ADR-0062 como parte da distribuição.

### 5. SecureGate: certificado de assinatura

- **Obrigatório em todo ambiente que não seja `Development` nem `Testing`**, Staging inclusive. Isso fecha o achado 6: nenhum ambiente implantável gera chave dentro do container.
- O certificado chega por **uma de duas** formas: `SecureGate:Signing:CertificatePath` (arquivo PKCS#12, o recomendado em compose via `secrets:`) ou `SecureGate:Signing:CertificateBase64` (o mesmo PKCS#12 em base64, para "só variável de ambiente"). `CertificatePassword` é opcional nos dois casos.
- As duas formas juntas, nenhuma das duas, base64 inválido ou PKCS#12 que não abre: a aplicação não sobe, com mensagem que diz qual chave conferir, sem ecoar o valor.
- O mesmo certificado assina e cifra, como hoje.
- Fica registrado no guia: o base64 em variável aparece em `docker inspect`. O caminho por arquivo de secret é o preferido; o base64 existe para quem só tem variável.
- **Rotação** (documentada, sem mecanismo novo): trocar o certificado invalida os tokens vigentes (no máximo 5 min de access token, ADR-0032), e os refresh tokens vivem no banco. Rotação sem queda fica no backlog.

### 6. NotificationHub: schema do Hangfire no `migrate`

- Um novo `ISeccoDatabaseMigrator` do NotificationHub (nome `hangfire`) instala ou atualiza o schema do Hangfire no banco de plataforma, chamando o instalador público do `Hangfire.SqlServer` (`SqlServerObjectsInstaller.Install`). O builder confirma a assinatura na 1.8.25.
- Credencial: `NotificationHub:BackgroundJobs:MigrationConnectionString` quando presente; senão, a `ConnectionString`. A credencial dona é opcional, para não obrigar duas credenciais num banco que não tem RLS.
- `AddSeccoBackgroundJobs` ganha um parâmetro opcional `prepareSchema` (padrão `true`, para não mudar outros consumidores). O NotificationHub passa `IsDevelopment()`: em DEV continua automático, fora dele `PrepareSchemaIfNecessary=false`.
- **Fora de DEV o NotificationHub recusa subir** se o schema do Hangfire estiver ausente ou numa versão menor que a exigida pelo pacote. A checagem é só de leitura, no molde do `EnsureSecureGateDatabaseMigratedAsync`, e a mensagem manda rodar `migrate`.
  - Se o pacote não expuser a versão exigida, a checagem vira "o schema existe", e a defasagem de versão fica coberta pelo passo `migrate` obrigatório do guia. O builder registra qual dos dois casos valeu.
- `SecureGate` e esta checagem são os dois únicos "recusa subir". LogStream e NotificationHub seguem a ADR-0038 para os bancos de tenant (migram no primeiro uso), e o guia explica por quê.

### 7. AdminPortal

- O Dockerfile novo segue o molde da seção 2. As referências entre produtos (clients do SecureGate e do LogStream) exigem `COPY` do client e do `openapi/` de cada um (memória: `ProjectReference` entre produtos exige `COPY` no Dockerfile).
- `services.AddDataProtection().SetApplicationName("secco-adminportal")` e, quando `AdminPortal:DataProtection:KeysPath` estiver definido, `.PersistKeysToFileSystem(...)`. Sem a chave, vale o padrão do framework, que serve ao F5.
- Healthcheck e proxy reverso como nas seções 3 e 4.
- **Sessões:** recriar o container perde o cofre em memória. O cookie continua decifrável, graças ao volume, mas aponta para uma sessão que não existe mais, e o operador volta pelo login. O guia diz isso com todas as letras. O SecureGate exige segundo fator do operador no authorize; se a sessão do SecureGate estiver viva, o builder confere se o operador precisa digitar o TOTP de novo e registra o resultado no guia.

### 8. Guia de implantação — `docs/guia-de-implantacao.md` (novo)

No formato do `docs/guia-de-deploy.md` da intranet (limitações primeiro, topologia, variáveis, compose, health, atualização). O `getting-started.md` ganha um link na seção 7. Conteúdo:

1. **Limitações conhecidas.** Host único, sessões do AdminPortal, base64 visível em `docker inspect`, tenants migrando no primeiro uso, e `ASPNETCORE_ENVIRONMENT=Production` também na homologação. Hoje vários comportamentos de segurança dependem de `IsProduction()` (cookie `Secure`, `RequireHttpsMetadata`, chave do catálogo), então "a mesma imagem" significa também o mesmo ambiente, e a diferença entre homologação e produção fica só na configuração.
2. **Pull.** As imagens são públicas, sem `docker login`. Tag fixa `X.Y.Z` e nunca `latest`; `X.Y` só para quem aceita receber patch.
3. **Topologia.** O proxy TLS publica o 443, os serviços ficam numa rede interna com subnet fixa e o banco é externo.
4. **Banco.** Um script SQL de exemplo (`deploy/sql/sqlserver-plataforma.sql`) cria os bancos de plataforma e os logins:
   - SecureGate: dono para o `migrate` e runtime com `db_datareader`/`db_datawriter`;
   - Hangfire do NotificationHub;
   - nenhum `sa` na aplicação.
   Os bancos de tenant vêm do provisionamento do SecureGate (ADR-0028) ou do DBA.
5. **Certificado de assinatura.** Como gerar o PKCS#12 com `openssl` e como entregá-lo, por arquivo de secret ou por base64.
6. **Tabela de variáveis por serviço**, com as obrigatórias marcadas.
   - SecureGate: as duas credenciais de banco (`MigrationConnectionString` dona e `ConnectionString` de runtime sem posse), certificado, e-mail, `PublicBaseUrl`, chave do catálogo, `PlatformClients` (LogStream, NotificationHub, AdminPortal, identidades de auditoria, quando houver), proxy e `AllowedHosts`.
   - Para cada outro serviço: a credencial de banco, `Secco:Authentication`, `Secco:SecureGate` (client de plataforma), e-mail no caso do NotificationHub, proxy e `AllowedHosts`.
   - O builder monta a tabela a partir das classes de options e dos validadores, nunca de memória.
7. **Compose de host único.** Fica em `deploy/docker-compose.yml` e `deploy/.env.example`: proxy Caddy com os domínios `login.exemplo.com.br`, `logs.exemplo.com.br`, `notificacoes.exemplo.com.br` e `admin.exemplo.com.br`; os quatro serviços; os serviços `*-migrate`; o volume do AdminPortal num bind mount do host com dono 1654; e a rede com subnet fixa.
   - O proxy recebe **aliases de rede** com os domínios públicos. Os serviços que chamam o SecureGate por HTTPS (exigência de Production) resolvem o domínio para o proxy dentro da rede, sem depender de *hairpin NAT* do host.
   - O `.env.example` segue a skill `env-file-format`.
8. **Ordem.** `migrate` do SecureGate, `up -d` do SecureGate e `/health/ready`, depois `migrate` de LogStream e NotificationHub (eles publicam o catálogo de permissões no SecureGate, ADR-0039, que precisa estar no ar), e por fim o `up -d` do resto. O compose codifica a ordem com `depends_on`, e o guia mostra os comandos.
9. **Primeiro operador**, pela API existente:
   - declarar um client de plataforma temporário `ClientCredentials` com `securegate:admin`;
   - rodar `migrate`;
   - criar a conta no tenant de plataforma (sai o convite por e-mail, ADR-0033) e atribuir `installation-operator`;
   - retirar o client da configuração e rodar `migrate` de novo, que o remove (ADR-0037).
   O builder **verifica que a API permite** os dois passos no tenant de plataforma. Se não permitir, para e me traz o achado, sem inventar um caminho novo.
10. **Health.** `/health/live`, `/health/ready` e o verbo `healthcheck`.
11. **Volumes e uid.** Só o AdminPortal (`/var/lib/secco-adminportal/keys`, uid 1654, 0700, tratado como segredo no backup). O SecureGate não precisa de volume (chaves no banco e certificado externo).
12. **Atualização e rollback por troca de tag.** Trocar a `SECCO_VERSION` no `.env`, rodar `pull`, `migrate` e `up -d`. Rollback é voltar a tag, com o aviso de que migration aplicada não é desfeita (ADR-0038 não cobre expand/contract): rollback para uma versão com schema anterior só com restauração de backup ou se a release disser que é compatível.
13. **Compatibilidade de versões.** A tabela também fica no CHANGELOG:

    | Plataforma (imagens) | `Secco.SecureGate.Client` | `Secco.LogStream.Client` | `Secco.NotificationHub.Client` |
    |---|---|---|---|
    | 0.1.0 | 0.26.0 | 0.5.0 | 0.6.0 |

### 9. Workflow `.github/workflows/publish-images.yml` (novo)

- **Gatilho:** `push` de tags `v*`, mais `workflow_dispatch` com a tag, como no de NuGet. `permissions: contents: read, packages: write, id-token: write, attestations: write`.
- **Job `verify`:**
  1. A tag casa com `^v[0-9]+\.[0-9]+\.[0-9]+$`. Pré-release fica fora do v1; se vier, o job reprova.
  2. **Guarda de compatibilidade:** para cada produto, o `openapi.json` no commit da tag é igual ao da última tag `<produto>-client/v*` alcançável. Se divergir, a release é reprovada com a mensagem "publique o client antes". Isso garante que toda imagem tem client correspondente, e a tabela é escrita a partir desse resultado no resumo do run.
  3. Build `linux/amd64` das quatro imagens com `load: true`, cache GHA (`type=gha`, escopo por imagem).
  4. Trivy em cada imagem com `severity: CRITICAL`, `exit-code: 1` e `ignore-unfixed: true`. O que não tem correção não reprova, mas aparece no relatório. A action é fixada por SHA de commit, não por tag.
  5. **Smoke** (`scripts/deploy-smoke.sh`, o mesmo do CI, seção 10) contra as imagens carregadas.
- **Job `publish`** (`needs: verify`, matriz por imagem): `docker/metadata-action` gera as tags `{{version}}` e `{{major}}.{{minor}}` (sem `latest`, `flavor: latest=false`) e as labels OCI `source`, `version`, `revision`, `title`, `description` e `licenses`. Depois vem o `docker/build-push-action` com `platforms: linux/amd64,linux/arm64`, `sbom: true`, `provenance: mode=max`, `push: true`, o mesmo cache e o `build-args` `SECCO_VERSION`.
- Actions de terceiros (`docker/*`, `aquasecurity/trivy-action`) ficam **fixadas por SHA** (ADR-0020, dependências novas e supply chain).
- O resumo do run lista as imagens, os digests e a tabela de compatibilidade.

### 10. Smoke do compose do guia — `scripts/deploy-smoke.sh` + `deploy/ci/`

É o que torna verdadeiro o critério "o compose do guia sobe a plataforma do zero". Roda no workflow de release (job `verify`) e num job novo `deploy-example` do `ci.yml` (filtro: `deploy/**`, Dockerfiles, `src/**` dos quatro produtos, SDK). O `docker-stacks` existente continua cobrindo o compose de dev.

- **Override de CI** (`deploy/ci/compose.ci.yml`): acrescenta um SQL Server de container no papel do "banco externo", que roda o mesmo `deploy/sql/sqlserver-plataforma.sql` do guia, e um MailHog como SMTP. Ele aponta as imagens para as tags carregadas.
- **TLS:** o script gera com `openssl` uma CA de teste e o certificado dos quatro domínios de exemplo, entrega-os ao Caddy (arquivos explícitos, sem ACME) e monta a CA nos serviços via `SSL_CERT_FILE`, que o OpenSSL do .NET respeita. Também gera o PKCS#12 de assinatura do SecureGate.
- **Asserções**, todas com `ASPNETCORE_ENVIRONMENT=Production`:
  1. Cada imagem roda como uid 1654 (`docker run --rm --entrypoint id <imagem> -u`).
  2. `docker run --rm <securegate> migrate` contra um servidor inalcançável sai **1**, e o log contém uma mensagem identificável, sem connection string.
  3. Os `migrate` do compose saem **0** contra o banco vazio.
  4. Os quatro serviços ficam `healthy` (HEALTHCHECK real) e respondem `200` em `/health/ready` através do proxy.
  5. Um token `client_credentials` emitido pelo SecureGate (client de plataforma do smoke, escopo `logstream`) é aceito pelo LogStream. Aceito quer dizer: com o token, uma rota protegida responde algo **diferente de `401`**, enquanto sem token ela responde `401`. Como não há tenant, `400` ou `403` provam que a autenticação passou. Depois de `up -d --force-recreate` do SecureGate e do LogStream, **o mesmo token continua aceito** e o JWKS tem o mesmo `kid` (o certificado é externo).
  6. Depois de recriar o AdminPortal, o arquivo de chave do DataProtection no bind mount é o mesmo (nome e hash).
  7. Nenhuma imagem contém `appsettings.Development.json` (`docker run --rm --entrypoint ls`).
- As sessões do SecureGate têm cobertura indireta: as chaves do DataProtection estão no banco, já testadas na ADR-0033, e a asserção 5 prova que o banco e o certificado sobrevivem. Um login interativo de ponta a ponta no smoke fica fora (exigiria navegador e TOTP).

### 11. Testes de código (ADR-0012)

- **SDK:**
  - `SeccoHealthCheckCommand`: mapeamento de status (200 → 0; 503, recusa e timeout → 1) contra um Kestrel local; parsing de porta (vazia, múltiplas, inválida, fora de faixa).
  - Proxy reverso: com `Enabled` e rede conhecida, `X-Forwarded-Proto: https` vira `Request.Scheme == "https"`; de uma origem fora da rede, é ignorado. `Enabled` sem rede, ou com CIDR inválido, não sobe.
- **SecureGate:** seleção do certificado (path, base64, os dois, nenhum, base64 inválido) em `Production` e `Staging`; `Development` e `Testing` inalterados.
- **NotificationHub** (Testcontainers MsSql): o `migrate` cria o schema do Hangfire num banco vazio; a subida fora de DEV com schema ausente recusa; com schema presente, sobe.
- **AdminPortal:** com `KeysPath` definido, a chave é gravada na pasta.
- **Template:** o job `validate-template` já constrói a imagem. O Dockerfile e o `Program.cs` do template recebem o mesmo molde (ADR-0013).

### 12. Fluxo de entrega

- ADR-0062 (distribuição por imagem: registro, nomes, tags, público, uma versão da plataforma, varredura antes do push, healthcheck por verbo, proxy reverso com rede obrigatória) e uma entrada no `design-decisions-log.md`.
- Plano em `docs/superpowers/plans/`.
- CHANGELOG: seção da plataforma (imagens), seções do SDK (`healthcheck`, proxy reverso, `prepareSchema`) e notas de upgrade:
  - Staging passa a exigir certificado;
  - o compose de dev precisa do `build.args`;
  - quem usa `Secco:ReverseProxy` precisa declarar a rede.
- `docs/roadmap.md`, `README.md` e `CLAUDE.md` (estado atual) atualizados.
- Build da solution em Release com 0 avisos, `dotnet test` da solution (memória: comando documentado, e procurar `error` na saída), job de smoke verde.
- **Release** (seguindo a skill `secco-platform-release`): primeiro as tags de pacote afetadas (`sdk/v0.11.0`, `templates/v…`, e o que a `check-release-chain.py` apontar, em levas de no máximo três), depois `v0.1.0`, que dispara as imagens.
- Depois do primeiro push, conferir no GHCR que os quatro pacotes ficaram **públicos e ligados ao repositório**. Se o GHCR criar o pacote privado, a visibilidade é trocada nas configurações do pacote, um passo manual que fica registrado no guia de release.

## O que fica com o builder

- Os nomes exatos das chaves de configuração novas, desde que sigam o padrão `<Produto>:<Área>:<Chave>` e as listadas aqui.
- O conteúdo da tabela de variáveis, extraído das options e dos validadores.
- A versão do Caddy e do Trivy, ambas fixadas.
- Como o script de smoke espera pelos serviços (polling com teto).
- Se o instalador do Hangfire expõe a versão exigida (seção 6) e se o operador precisa de TOTP de novo depois de recriar o AdminPortal (seção 7). Os dois resultados vão para o guia.

## Riscos de segurança (ADR-0020)

| Ponto | Risco | Tratamento |
|---|---|---|
| Imagem pública | Segredo de dev embarcado | `appsettings.Development.json` fora da imagem de release, com asserção no smoke |
| Proxy reverso | `X-Forwarded-*` forjado por quem alcança a porta | Desligado por padrão; rede conhecida obrigatória; `ForwardLimit=1`; `Host` não é reescrito |
| Certificado em base64 | Visível em `docker inspect` e no ambiente do processo | Documentado; arquivo de secret como caminho preferido |
| Healthcheck | Uma porta vinda do ambiente montaria a URL | Inteiro validado; destino fixo em `127.0.0.1`; só lê o status |
| Supply chain do workflow | Action de terceiro comprometida | Fixadas por SHA; Trivy reprova em CRITICAL antes do push; SBOM e provenance publicados |
| Hangfire | Runtime com DDL | `migrate` com credencial dona opcional; runtime sem `PrepareSchema` fora de DEV |
| Primeiro operador | Client com `securegate:admin` esquecido na configuração | O guia manda retirá-lo e rodar `migrate` em seguida, e o seed o remove |
