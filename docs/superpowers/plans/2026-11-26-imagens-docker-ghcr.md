# Imagens Docker versionadas no GHCR — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** publicar no GHCR as imagens `secco-securegate`, `secco-logstream`, `secco-notificationhub` e `secco-adminportal`, versionadas por tag `vX.Y.Z`, com um guia e um compose de host único que o CI comprova.

**Architecture:** o SDK ganha o verbo `healthcheck` e o suporte opt-in a proxy reverso. O SecureGate passa a exigir certificado fora de DEV e Testing, e o NotificationHub leva o schema do Hangfire para o `migrate`. O AdminPortal fixa a pasta das chaves do DataProtection. Os Dockerfiles seguem um molde único (build no `BUILDPLATFORM`, não-root, `HEALTHCHECK`, sem `appsettings.Development.json`). Um workflow novo constrói, varre (Trivy), roda o smoke do compose do guia e só então publica multi-arch com SBOM e provenance.

**Tech Stack:** .NET 10, ASP.NET Core `ForwardedHeaders`, OpenIddict, Hangfire.SqlServer 1.8.25, Docker BuildKit, `docker/build-push-action`, `docker/metadata-action`, `aquasecurity/trivy-action`, Caddy, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** [`docs/superpowers/specs/2026-11-26-imagens-docker-ghcr-design.md`](../specs/2026-11-26-imagens-docker-ghcr-design.md)

## Global Constraints

- Imagens: `ghcr.io/<github.repository_owner>/secco-securegate|secco-logstream|secco-notificationhub|secco-adminportal`, **públicas**.
- Tags `X.Y.Z` e `X.Y`; **nunca** `latest` (`flavor: latest=false`). Só tags `^v[0-9]+\.[0-9]+\.[0-9]+$`.
- Uma versão da plataforma para as quatro imagens. Primeira release: `v0.1.0`.
- Base `mcr.microsoft.com/dotnet/aspnet:10.0` (nunca Alpine), `USER $APP_UID` (uid **1654**), `ASPNETCORE_HTTP_PORTS=8080`.
- Plataformas `linux/amd64,linux/arm64`; o estágio de build roda em `--platform=$BUILDPLATFORM`.
- Imagem de release **sem** `appsettings.Development.json` (`ARG SECCO_INCLUDE_DEV_SETTINGS=false`).
- Nenhuma credencial de feed no build: o restore só usa o nuget.org.
- Actions de terceiros fixadas por **SHA de commit**.
- Trivy: `severity: CRITICAL`, `exit-code: 1`, `ignore-unfixed: true`, **antes** de qualquer push.
- Único `VOLUME`: `/var/lib/secco-adminportal/keys` (uid 1654, modo 0700).
- Configuração nova: `Secco:ReverseProxy:Enabled` (padrão `false`), `Secco:ReverseProxy:KnownNetworks` (CIDR, obrigatória quando `Enabled`), `SecureGate:Signing:CertificateBase64`, `NotificationHub:BackgroundJobs:MigrationConnectionString` (opcional), `AdminPortal:DataProtection:KeysPath`.
- ADR nova: **ADR-0062**; a ADR-0060 está reservada em outro branch.
- Build `dotnet build Secco.Platform.slnx -c Release` com 0 avisos; `dotnet test Secco.Platform.slnx -c Release`, procurando `error` na saída (o AdminPortal.Tests quebra em Debug com SDK 11).
- Commits Conventional Commits, direto na `main`, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **`ASPNETCORE_URLS` definido pelo adotante** (ex.: `http://+:5000`) sobrepõe `ASPNETCORE_HTTP_PORTS`. Sem tratar isso, o probe bate na 8080 e o container fica `unhealthy` para sempre. O probe deve usar a porta do primeiro URL `http://` de `ASPNETCORE_URLS` quando ele existir (teste na Task 1).
2. **Proxy conectando por endereço IPv4 mapeado em IPv6** (`::ffff:172.30.0.5`): a rede `172.30.0.0/24` precisa casar, senão os `X-Forwarded-*` são ignorados em silêncio e o OIDC quebra (teste na Task 2).
3. **Base64 do certificado com quebras de linha**, como sai do `base64` sem `-w0`: deve ser aceito (teste na Task 4).
4. **`migrate` contra servidor inalcançável** deve terminar em menos de 90 s, sair 1 e logar uma mensagem identificável, sem connection string, mesmo com a política de retry do EF (asserção no smoke da Task 9).
5. **`AllowedHosts` fixado no domínio público**: o probe envia `Host: localhost`, e o guia manda incluir `localhost` (teste na Task 1 de que o `Host` enviado é `localhost`; asserção no smoke, que usa `AllowedHosts` com domínio + `localhost`).

---

### Task 1: SDK — verbo `healthcheck`

**Files:**
- Create: `src/SDK/Secco.SDK.AspNetCore/HealthChecks/SeccoHealthCheckCommand.cs`
- Test: `tests/SDK/Secco.SDK.AspNetCore.Tests/HealthChecks/SeccoHealthCheckCommandTests.cs`

**Interfaces:**
- Produces:
  - `public static class SeccoHealthCheckCommand` (namespace `Secco.SDK.AspNetCore.HealthChecks`):
    - `public const string Verb = "healthcheck";`
    - `public static bool IsHealthCheck(string[] args)` — verdadeiro quando `args[0]` é exatamente `Verb`, pelo mesmo critério do `SeccoCommands.IsMigrate`.
    - `public static Task<int> RunAsync(CancellationToken cancellationToken = default)` — lê o ambiente do processo e chama a sobrecarga interna.
    - `internal static int? ResolvePort(string? urls, string? httpPorts)` — com `urls` não vazio, usa a porta do primeiro item `http://` (separador `;`) e ignora `https://`; senão, o primeiro item de `httpPorts`; com os dois vazios, 8080. Devolve `null` para valor que não seja inteiro de 1 a 65535 e para host curinga sem porta.
    - `internal static Task<int> RunAsync(int port, HttpMessageHandler? handler, TimeSpan timeout, CancellationToken cancellationToken)` — GET em `http://127.0.0.1:{port}/health/ready` com `Host: localhost`. `200` → 0; qualquer outra coisa → 1, com uma linha no stderr (`healthcheck: <status ou tipo da exceção>`). O timeout padrão de `RunAsync()` é 5 s.

- [ ] **Step 1: Write the failing tests**
  - `IsHealthCheck_PrimeiroArgumentoExato_True` (`["healthcheck"]` → true; `["HealthCheck"]`, `[]`, `["migrate","healthcheck"]` → false)
  - `ResolvePort_SemVariaveis_8080`
  - `ResolvePort_HttpPortsMultiplos_UsaOPrimeiro` (`"8081;8082"` → 8081)
  - `ResolvePort_UrlsSobrepoeHttpPorts` (`urls="https://+:443;http://+:5000"`, `httpPorts="8080"` → 5000)
  - `ResolvePort_Invalido_Null` (`"abc"`, `"0"`, `"70000"`, `"-1"` → null)
  - `RunAsync_Status200_Zero` e `RunAsync_Status503_Um`, com um `HttpMessageHandler` falso que também afirma `request.RequestUri == http://127.0.0.1:8080/health/ready` e `request.Headers.Host == "localhost"`
  - `RunAsync_ConexaoRecusada_Um` (o handler lança `HttpRequestException`)
  - `RunAsync_Timeout_Um` (o handler espera além de um timeout de 100 ms)

- [ ] **Step 2:** `dotnet test tests/SDK/Secco.SDK.AspNetCore.Tests --filter SeccoHealthCheckCommandTests` → FAIL (o tipo não existe).
- [ ] **Step 3:** implementar em `SeccoHealthCheckCommand.cs`. O `HttpClient` é criado por chamada, sem `IHttpClientFactory`, porque o host não existe ainda.
- [ ] **Step 4:** o mesmo comando → PASS.
- [ ] **Step 5:** commit `feat(sdk): verbo healthcheck para HEALTHCHECK de container sem curl`.

### Task 2: SDK — proxy reverso opt-in

**Files:**
- Create: `src/SDK/Secco.SDK.AspNetCore/ReverseProxy/SeccoReverseProxyOptions.cs`, `src/SDK/Secco.SDK.AspNetCore/ReverseProxy/SeccoReverseProxyOptionsValidator.cs`, `src/SDK/Secco.SDK.AspNetCore/Extensions/SeccoReverseProxyExtensions.cs`
- Modify: `src/SDK/Secco.SDK.AspNetCore/Extensions/SeccoPlatformServiceCollectionExtensions.cs` (acrescentar `services.AddSeccoForwardedHeaders();`)
- Test: `tests/SDK/Secco.SDK.AspNetCore.Tests/ReverseProxy/SeccoReverseProxyTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class SeccoReverseProxyOptions { public const string SectionKey = "Secco:ReverseProxy"; public bool Enabled { get; set; } public List<string> KnownNetworks { get; set; } = []; }`
  - `public static IServiceCollection AddSeccoForwardedHeaders(this IServiceCollection services)` — idempotente. Liga as options à seção com `ValidateOnStart` e o validador. Configura `ForwardedHeadersOptions` por `IConfigureOptions` lendo as options: `ForwardedHeaders = XForwardedFor | XForwardedProto`, `ForwardLimit = 1`, `KnownNetworks` limpas e preenchidas pelos CIDR (`System.Net.IPNetwork.Parse`), `KnownProxies` limpas.
  - `public static IApplicationBuilder UseSeccoForwardedHeaders(this IApplicationBuilder app)` — chama `UseForwardedHeaders()` só quando `Enabled`; senão, é no-op.
- Validador: com `Enabled` e `KnownNetworks` vazia, falha com a mensagem `"Secco:ReverseProxy:KnownNetworks é obrigatória quando Enabled=true (ADR-0062)."`. Um CIDR que não parseia falha com `"Secco:ReverseProxy:KnownNetworks:{i} não é um CIDR válido."`, sem ecoar além do índice.

- [ ] **Step 1: Write the failing tests** (TestServer mínimo com o endpoint `/scheme` devolvendo `Request.Scheme` e `Connection.RemoteIpAddress`; o IP remoto é injetado por um middleware de teste antes do `UseSeccoForwardedHeaders`):
  - `Desligado_IgnoraXForwardedProto` (sem configuração, `X-Forwarded-Proto: https` → `http`)
  - `Ligado_OrigemNaRede_AplicaProtoEIp` (rede `172.30.0.0/24`, remoto `172.30.0.5`, `X-Forwarded-Proto: https` e `X-Forwarded-For: 203.0.113.9` → `https` e `203.0.113.9`)
  - `Ligado_OrigemIpv4MapeadaEmIpv6_Aplica` (remoto `::ffff:172.30.0.5` → `https`)
  - `Ligado_OrigemForaDaRede_Ignora` (remoto `10.0.0.5` → `http`)
  - `Ligado_SemRede_NaoSobe` (o `StartAsync` do host lança `OptionsValidationException` com a mensagem acima)
  - `Ligado_CidrInvalido_NaoSobe` (`"300.1.1.1/8"`)
  - `AddSeccoPlatform_RegistraForwardedHeaders` (com `AddSeccoPlatform()` e `Enabled`, a resolução de `IOptions<ForwardedHeadersOptions>` traz a rede)
- [ ] **Step 2:** `dotnet test tests/SDK/Secco.SDK.AspNetCore.Tests --filter SeccoReverseProxyTests` → FAIL.
- [ ] **Step 3:** implementar. Se o teste do IPv4 mapeado falhar com o middleware do framework, a correção é acrescentar também a forma mapeada de cada rede IPv4 (`::ffff:a.b.c.d/96+prefixo`) às `KnownNetworks`.
- [ ] **Step 4:** o mesmo comando → PASS; `dotnet test tests/SDK/Secco.SDK.AspNetCore.Tests` inteiro → PASS.
- [ ] **Step 5:** commit `feat(sdk): suporte opt-in a proxy reverso com rede conhecida obrigatória`.

### Task 3: SDK — Hangfire sem preparar o schema

**Files:**
- Modify: `src/SDK/Secco.SDK.AspNetCore/Extensions/SeccoBackgroundJobsServiceCollectionExtensions.cs`
- Test: `tests/SDK/Secco.SDK.AspNetCore.Tests/BackgroundJobs/` (arquivo existente das extensões, ou `SeccoBackgroundJobsPrepareSchemaTests.cs`)

**Interfaces:**
- Produces: a sobrecarga nova `public static IServiceCollection AddSeccoBackgroundJobs(this IServiceCollection services, Func<IServiceProvider, string> platformConnectionStringFactory, bool prepareSchema)`. A sobrecarga existente delega com `prepareSchema: true`; uma sobrecarga, e não um parâmetro opcional, para não quebrar o binário de quem já compilou contra o pacote. O valor vai para `UseSqlServerStorage(connectionString, new SqlServerStorageOptions { PrepareSchemaIfNecessary = prepareSchema })`.

- [ ] **Step 1:** teste `AddSeccoBackgroundJobs_PrepareSchemaFalse_NaoTocaNoBanco`: com uma connection string para um servidor inexistente (`Server=127.0.0.1,1;Connect Timeout=1`), resolver `JobStorage` não lança quando `prepareSchema: false`. Com `true`, a construção tenta conectar e lança; o teste afirma os dois casos.
- [ ] **Step 2:** rodar → FAIL (a sobrecarga não existe).
- [ ] **Step 3:** implementar.
- [ ] **Step 4:** rodar → PASS.
- [ ] **Step 5:** commit `feat(sdk): AddSeccoBackgroundJobs sem preparar o schema do Hangfire`.

### Task 4: SecureGate — certificado de assinatura fora de DEV/Testing

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Api/Extensions/SigningCertificateSource.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/Extensions/SecureGateOpenIddictExtensions.cs` (`ConfigureCertificates`)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/SigningCertificateSourceTests.cs`

**Interfaces:**
- Produces: `internal static class SigningCertificateSource { internal static X509Certificate2? Load(IConfiguration configuration, IHostEnvironment environment); }`.
  - Devolve `null` em `Development` e em `Testing`. Nesses casos `ConfigureCertificates` mantém o comportamento atual: chaves efêmeras em Testing, certificado de desenvolvimento em Development.
  - Em qualquer outro ambiente, lê `SecureGate:Signing:CertificatePath` **ou** `SecureGate:Signing:CertificateBase64` (`CertificatePassword` opcional) e carrega com `X509CertificateLoader.LoadPkcs12FromFile` ou `LoadPkcs12`.
  - Lança `InvalidOperationException` quando os dois estão presentes, quando nenhum está, quando o base64 é inválido ou quando o PKCS#12 não abre. A mensagem cita a chave de configuração e nunca o valor; o erro original vai como `InnerException`.
  - Sem chave privada, também lança.
- `ConfigureCertificates`: quando `Load` devolve um certificado, `AddSigningCertificate` + `AddEncryptionCertificate` (como hoje em Production). O ramo `DisableTransportSecurityRequirement` (`!IsProduction()`) **não muda**.

- [ ] **Step 1: Write the failing tests** (PKCS#12 gerado no teste com `CertificateRequest` + `CreateSelfSigned` + `Export(Pkcs12, senha)`, gravado num temporário):
  - `Load_Development_Null` e `Load_Testing_Null`
  - `Load_ProductionComPath_Carrega` e `Load_StagingComPath_Carrega`
  - `Load_ProductionComBase64_Carrega`
  - `Load_Base64ComQuebrasDeLinha_Carrega` (base64 quebrado a cada 76 colunas com `\n`)
  - `Load_StagingSemNada_Lanca` (a mensagem contém `SecureGate:Signing:CertificatePath`)
  - `Load_PathEBase64_Lanca`
  - `Load_Base64Invalido_Lanca` (a mensagem não contém o valor)
  - `Load_SenhaErrada_Lanca`
- [ ] **Step 2:** `dotnet test tests/SecureGate/Secco.SecureGate.Tests --filter SigningCertificateSourceTests` → FAIL.
- [ ] **Step 3:** implementar e religar `ConfigureCertificates`.
- [ ] **Step 4:** o mesmo comando → PASS; `--filter ClientCredentialsFlowTests` → PASS (Testing segue efêmero).
- [ ] **Step 5:** commit `feat(securegate): certificado de assinatura obrigatório fora de DEV e Testing, por arquivo ou base64`.

### Task 5: NotificationHub — schema do Hangfire no `migrate`

**Files:**
- Create: `src/NotificationHub/Secco.NotificationHub.Infrastructure/BackgroundJobs/HangfireSchemaMigrator.cs`, `src/NotificationHub/Secco.NotificationHub.Infrastructure/BackgroundJobs/HangfireSchemaCheck.cs`
- Modify: `src/NotificationHub/Secco.NotificationHub.Infrastructure/NotificationHubInfrastructureExtensions.cs` (`AddNotificationHubBackgroundJobs`, e a classe `NotificationHubBackgroundJobOptions` ganha `MigrationConnectionString`), `src/NotificationHub/Secco.NotificationHub.Api/Program.cs`
- Test: `tests/NotificationHub/Secco.NotificationHub.Tests/Integration/HangfireSchemaTests.cs`

**Interfaces:**
- Consumes: a sobrecarga `AddSeccoBackgroundJobs(..., bool prepareSchema)` da Task 3 e `ISeccoDatabaseMigrator` (`Name`, `MigrateAsync` → alvos que falharam).
- Produces:
  - `AddNotificationHubBackgroundJobs(this IServiceCollection services, bool prepareSchema)` (substitui a sem parâmetro; o Program passa `builder.Environment.IsDevelopment()`). Registra o `HangfireSchemaMigrator` como `ISeccoDatabaseMigrator`.
  - `internal sealed class HangfireSchemaMigrator : ISeccoDatabaseMigrator` — `Name = "NotificationHub (Hangfire)"`. Abre uma `SqlConnection` com `MigrationConnectionString ?? ConnectionString` e chama `SqlServerObjectsInstaller.Install(connection)`. Exceção vira o alvo `"hangfire"` na lista de falhas, com log da exceção e sem a connection string.
  - `public static Task EnsureNotificationHubJobSchemaAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)` — só leitura. Lança `InvalidOperationException("O schema do Hangfire do NotificationHub está ausente ou desatualizado. Rode 'dotnet Secco.NotificationHub.Api.dll migrate' antes de subir (ADR-0062).")` quando `[HangFire].[Schema]` não existe ou tem `Version` menor que a exigida pelo pacote. Primeiro passo da implementação: conferir se o `Hangfire.SqlServer` 1.8.25 expõe a versão exigida (ex.: constante pública no `SqlServerObjectsInstaller`). Se não expuser, a checagem é só "a tabela existe com uma linha", e isso fica registrado no comentário e no guia.
- `Program.cs`: no ramo fora de DEV (novo `else` depois do `if (IsDevelopment())`), `await app.Services.EnsureNotificationHubJobSchemaAsync();`, sob a chave `NotificationHub:BackgroundJobs:VerifySchemaOnStartup` (padrão `true`, como a `VerifyMigrationsOnStartup` do SecureGate).

- [ ] **Step 1: Write the failing tests** (Testcontainers MsSql, banco vazio criado no teste):
  - `Migrate_BancoVazio_CriaSchemaDoHangfire` (depois de `MigrateAsync`, `[HangFire].[Schema]` existe e a lista de falhas está vazia)
  - `Migrate_Repetido_Idempotente`
  - `Migrate_ServidorInalcancavel_DevolveAlvoHangfire` (a lista é `["hangfire"]`)
  - `Migrate_UsaMigrationConnectionStringQuandoPresente` (a de runtime aponta para um servidor inexistente e a de migração para o container → sucesso)
  - `EnsureSchema_Ausente_Lanca` e `EnsureSchema_Presente_NaoLanca`
- [ ] **Step 2:** `dotnet test tests/NotificationHub/Secco.NotificationHub.Tests --filter HangfireSchemaTests` → FAIL.
- [ ] **Step 3:** implementar e religar o Program.
- [ ] **Step 4:** o mesmo comando → PASS; `dotnet test tests/NotificationHub/Secco.NotificationHub.Tests` inteiro → PASS. A `NotificationHubApiFactory` roda em Development; se rodar em outro ambiente, desligar a checagem pela chave.
- [ ] **Step 5:** commit `feat(notificationhub): schema do Hangfire aplicado pelo migrate e conferido na subida fora de DEV`.

### Task 6: AdminPortal — chaves do DataProtection em pasta configurável

**Files:**
- Create: `src/AdminPortal/Secco.AdminPortal/Services/AdminPortalDataProtectionExtensions.cs`
- Modify: `src/AdminPortal/Secco.AdminPortal/Program.cs`
- Test: `tests/AdminPortal/Secco.AdminPortal.Tests/DataProtectionTests.cs`

**Interfaces:**
- Produces: `internal static IServiceCollection AddAdminPortalDataProtection(this IServiceCollection services, IConfiguration configuration)` — `AddDataProtection().SetApplicationName("secco-adminportal")` e, quando `AdminPortal:DataProtection:KeysPath` não estiver vazio, `.PersistKeysToFileSystem(new DirectoryInfo(path))`.

- [ ] **Step 1:** teste `KeysPath_Definido_GravaChaveNaPasta`: um host mínimo com a pasta temporária; depois de `IDataProtectionProvider.CreateProtector("t").Protect("x")`, a pasta contém um `key-*.xml`. Segundo teste, `KeysPath_Definido_ReabreComAMesmaChave`: um segundo provider na mesma pasta faz `Unprotect` do payload do primeiro.
- [ ] **Step 2:** rodar com `-c Release` → FAIL.
- [ ] **Step 3:** implementar e chamar no Program logo depois de `AddSeccoHealthChecks()`.
- [ ] **Step 4:** `dotnet test tests/AdminPortal/Secco.AdminPortal.Tests -c Release` → PASS.
- [ ] **Step 5:** commit `feat(adminportal): chaves do DataProtection em pasta configurável`.

### Task 7: Program.cs — `healthcheck` e proxy nos quatro produtos e no template

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Api/Program.cs`, `src/LogStream/Secco.LogStream.Api/Program.cs`, `src/NotificationHub/Secco.NotificationHub.Api/Program.cs`, `src/AdminPortal/Secco.AdminPortal/Program.cs`, `templates/secco-service/Secco.SampleService.Api/Program.cs`
- Test: `tests/SDK/Secco.SDK.AspNetCore.Tests/Platform/SeccoPlatformTests.cs` (sem teste novo de produto: o comportamento está coberto nas Tasks 1 e 2, e a integração vem pelo smoke da Task 9)

**Interfaces:**
- Consumes: `SeccoHealthCheckCommand.IsHealthCheck` e `RunAsync` (Task 1); `AddSeccoForwardedHeaders` e `UseSeccoForwardedHeaders` (Task 2).

- [ ] **Step 1:** em cada `Program.cs`, a primeira instrução passa a ser `if (SeccoHealthCheckCommand.IsHealthCheck(args)) { return await SeccoHealthCheckCommand.RunAsync(); }`. O AdminPortal passa a terminar com `return 0;`.
- [ ] **Step 2:** em cada `Program.cs`, `app.UseSeccoForwardedHeaders();` como **primeira** linha depois de `builder.Build()`. O AdminPortal também chama `builder.Services.AddSeccoForwardedHeaders();`, porque não usa `AddSeccoPlatform()`.
- [ ] **Step 3:** `dotnet build Secco.Platform.slnx -c Release` → 0 avisos, 0 erros.
- [ ] **Step 4:** prova manual: `dotnet run --project src/LogStream/Secco.LogStream.Api -- healthcheck` com nada escutando na 8080 → código de saída 1 em menos de 6 s.
- [ ] **Step 5:** commit `feat(platform): verbo healthcheck e proxy reverso nos produtos e no template`.

### Task 8: Dockerfiles no molde de release

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Api/Dockerfile`, `src/LogStream/Secco.LogStream.Api/Dockerfile`, `src/NotificationHub/Secco.NotificationHub.Api/Dockerfile`, `templates/secco-service/Secco.SampleService.Api/Dockerfile`, `docker-compose.yml` (raiz), `templates/secco-service/docker-compose.yml`
- Create: `src/AdminPortal/Secco.AdminPortal/Dockerfile`

**Interfaces:**
- Produces: build args `SECCO_VERSION` (padrão `0.0.0`) e `SECCO_INCLUDE_DEV_SETTINGS` (padrão `false`); `HEALTHCHECK` pelo verbo; o `VOLUME` do AdminPortal.

Molde (os `COPY` de cada produto ficam como estão; o AdminPortal copia `src/SharedKernel/`, `src/SDK/`, `src/SecureGate/Secco.SecureGate.Client/`, `src/SecureGate/Secco.SecureGate.Api/openapi/`, `src/LogStream/Secco.LogStream.Client/`, `src/LogStream/Secco.LogStream.Api/openapi/` e `src/AdminPortal/`):

```dockerfile
# syntax=docker/dockerfile:1
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG SECCO_VERSION=0.0.0
ARG SECCO_INCLUDE_DEV_SETTINGS=false
WORKDIR /src
# ... COPY do produto ...
RUN dotnet publish <csproj> --configuration Release --output /app \
    /p:UseAppHost=false /p:MinVerSkip=true /p:Version=${SECCO_VERSION} \
 && if [ "$SECCO_INCLUDE_DEV_SETTINGS" != "true" ]; then rm -f /app/appsettings.Development.json; fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "<Assembly>.dll", "healthcheck"]
ENTRYPOINT ["dotnet", "<Assembly>.dll"]
```

No AdminPortal, antes do `USER`: `RUN mkdir -p /var/lib/secco-adminportal/keys && chown 1654:1654 /var/lib/secco-adminportal/keys && chmod 0700 /var/lib/secco-adminportal/keys`, `ENV AdminPortal__DataProtection__KeysPath=/var/lib/secco-adminportal/keys` e `VOLUME /var/lib/secco-adminportal/keys`.

- [ ] **Step 1:** reescrever os quatro Dockerfiles existentes e criar o do AdminPortal.
- [ ] **Step 2:** nos `x-securegate`, `x-logstream` e `x-notificationhub` do `docker-compose.yml` da raiz, e no do template, `build.args: { SECCO_INCLUDE_DEV_SETTINGS: "true" }`.
- [ ] **Step 3:** build local de cada imagem: `docker build -f <Dockerfile> -t secco-<produto>:dev --build-arg SECCO_VERSION=0.1.0 .` → sucesso nas quatro.
- [ ] **Step 4:** conferências por imagem:
  - `docker run --rm --entrypoint id secco-<produto>:dev -u` → `1654`;
  - `docker run --rm --entrypoint ls secco-<produto>:dev /app/appsettings.Development.json` → código diferente de 0;
  - `docker inspect -f '{{json .Config.Healthcheck}}'` → mostra o verbo.
- [ ] **Step 5:** `docker compose --profile all up -d --build` → as três APIs ficam `healthy`; depois `docker compose --profile all down -v`.
- [ ] **Step 6:** commit `build(platform): Dockerfiles no molde de release — não-root, healthcheck, sem settings de dev`.

### Task 9: compose do guia e smoke

**Files:**
- Create:
  - `deploy/docker-compose.yml`
  - `deploy/.env.example`
  - `deploy/Caddyfile`
  - `deploy/sql/sqlserver-plataforma.sql`
  - `deploy/ci/compose.ci.yml`
  - `deploy/ci/Caddyfile.ci` (certificados explícitos em vez de ACME)
  - `scripts/deploy-smoke.sh`

**Interfaces:**
- Consumes: as imagens da Task 8 (variável `SECCO_IMAGE_PREFIX`, padrão `ghcr.io/rafsecco`, e `SECCO_VERSION`).
- Produces: `scripts/deploy-smoke.sh <prefixo> <versao>` → sai 0 só se todas as asserções passarem. O script imprime `docker compose ps`/`logs` na falha e sempre faz `down -v` no fim.

Decisões fixadas:
- **Rede** `secco` com `ipam.config.subnet: 172.30.0.0/24`. Todos os serviços com `Secco__ReverseProxy__Enabled=true` e `Secco__ReverseProxy__KnownNetworks__0=172.30.0.0/24`.
- **Proxy** `caddy:2` (fixado por digest) com aliases de rede `login.exemplo.com.br`, `logs.exemplo.com.br`, `notificacoes.exemplo.com.br` e `admin.exemplo.com.br`. Só ele publica `443` e `80`.
- **Serviços** `securegate-migrate`, `securegate`, `logstream-migrate`, `logstream`, `notificationhub-migrate`, `notificationhub` e `adminportal`. A ordem vai por `depends_on`: `securegate` `service_healthy` antes dos `migrate` de LogStream e NotificationHub.
- **Volume do AdminPortal:** bind mount `./data/adminportal-keys:/var/lib/secco-adminportal/keys`; o guia manda `chown 1654:1654` e `chmod 700`.
- **SQL** (`sqlserver-plataforma.sql`): cria `secco_securegate` (login `secco_securegate_migrator` com `db_owner`, `secco_securegate_app` com `db_datareader`/`db_datawriter`) e `secco_notificationhub_jobs` (`secco_nh_jobs_migrator` com `db_owner`, `secco_nh_jobs_app` com `db_datareader`/`db_datawriter` + `EXECUTE` no schema `HangFire`). As senhas vêm de variáveis `sqlcmd` (`$(SECUREGATE_MIGRATOR_PASSWORD)` etc.), nunca literais.
- **`PlatformClients` do SecureGate:** `logstream-service` (catálogo, `authorization:read`, `permissions:logstream`), `notificationhub-service` (idem para o notificationhub), `adminportal` (`AuthorizationCode`, redirect `https://admin.exemplo.com.br/signin-oidc`) e, só no CI, `smoke-client` (`logstream`). Escopos e papéis exatos conferidos contra o `getting-started.md` e o `appsettings.Development.json` do SecureGate.
- **Override de CI:** o serviço `sqlserver` (mesma imagem do compose de dev) com um passo `init` que roda o script SQL; `mailhog`; `SSL_CERT_FILE=/certs/ca-bundle.crt` montado em todos os serviços; e o certificado de assinatura por `SecureGate__Signing__CertificateBase64`.

- [ ] **Step 1:** escrever o compose, o `.env.example` (seguindo a skill `env-file-format`), o Caddyfile e o SQL.
- [ ] **Step 2:** escrever o `deploy-smoke.sh`:
  - gera a CA, o certificado dos quatro domínios e o PKCS#12 de assinatura com `openssl` num diretório temporário;
  - monta o `ca-bundle.crt` (bundle do sistema + CA de teste);
  - faz as asserções 1 a 7 da seção 10 do design, mais a do Review Focus 4: `timeout 90 docker run --rm ... migrate` contra `Server=10.255.255.1` → código 1, e o log contém `Migrations:` e não contém `Password=`.
- [ ] **Step 3: verificar o primeiro operador pela API.** Com o stack de pé, usando um client `ClientCredentials` com `securegate:admin` acrescentado ao override de CI:
  - criar uma conta no tenant de plataforma;
  - atribuir `installation-operator`.
  O script afirma os dois `2xx`. **Se a API recusar algum dos dois passos, parar e levar o achado ao Rafael (design, seção 8.9).**
- [ ] **Step 4:** construir as imagens locais com o prefixo `local` e rodar `bash scripts/deploy-smoke.sh local 0.1.0` → sai 0.
- [ ] **Step 5:** commit `feat(deploy): compose de host único do guia e smoke que o exercita`.

### Task 10: workflows

**Files:**
- Create: `.github/workflows/publish-images.yml`, `scripts/check-image-contracts.sh`
- Modify: `.github/workflows/ci.yml` (job `deploy-example`; filtro `deploy` em `changes` com `deploy/**`, `scripts/deploy-smoke.sh`, `**/Dockerfile`)

**Interfaces:**
- Consumes: `scripts/deploy-smoke.sh` (Task 9).
- Produces: `scripts/check-image-contracts.sh` — para SecureGate, LogStream e NotificationHub, encontra a última tag `<produto-minúsculo>-client/v*` alcançável a partir de `HEAD` (`git tag --merged HEAD | sort -V | tail -1`) e roda `git diff --quiet <tag> HEAD -- src/<P>/Secco.<P>.Api/openapi/`. Sai 1 na primeira divergência, com `contrato de <P> diverge de <tag>: publique o client antes da imagem`. Em sucesso, imprime a linha da tabela de compatibilidade em Markdown.

- [ ] **Step 1:** `scripts/check-image-contracts.sh` + execução local → imprime `| 0.1.0 | 0.26.0 | 0.5.0 | 0.6.0 |` quando chamado com `0.1.0`.
- [ ] **Step 2:** `publish-images.yml`, conforme a seção 9 do design:
  - gatilho `push.tags: ["v*"]` e `workflow_dispatch.inputs.tag`;
  - `env.SECCO_TAG: ${{ inputs.tag || github.ref_name }}`;
  - job `verify`: valida o formato da tag, roda `check-image-contracts.sh` e grava a tabela no `GITHUB_STEP_SUMMARY`; depois `docker/setup-buildx-action`, quatro `build-push-action` com `load: true`, `platforms: linux/amd64`, `cache-to/from: type=gha,scope=<imagem>`, `tags: local/secco-<p>:${version}`; depois o `trivy-action` por imagem; e por fim o `deploy-smoke.sh local ${version}`;
  - job `publish` (`needs: verify`, matriz `{name, dockerfile}`): `docker/login-action` no `ghcr.io` com `GITHUB_TOKEN`, `metadata-action` (`images: ghcr.io/${{ github.repository_owner }}/secco-<p>`, `tags: type=semver,pattern={{version}}` e `type=semver,pattern={{major}}.{{minor}}`, `flavor: latest=false`), `build-push-action` com `platforms: linux/amd64,linux/arm64`, `push: true`, `sbom: true`, `provenance: mode=max`, `build-args: SECCO_VERSION=<versão>` e o mesmo cache. O digest vai para o resumo do run.
  - Todas as actions `uses:` por SHA, com a versão em comentário.
- [ ] **Step 3:** job `deploy-example` no `ci.yml`: constrói as quatro imagens com o prefixo `local` e roda o smoke. `if`: `deploy`, `platform`, `securegate`, `logstream`, `notificationhub` ou `adminportal` mudaram.
- [ ] **Step 4:** validar a sintaxe com `actionlint` (via `docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:latest`) → sem erros.
- [ ] **Step 5:** commit `ci(platform): publicação de imagens no GHCR por tag v* e smoke do compose do guia`.

### Task 11: ADR, guia e documentação de entrega

**Files:**
- Modify: `docs/adr/secco-platform-adrs.md` (ADR-0062), `docs/design-decisions-log.md`, `docs/getting-started.md` (seção 7 linka o guia), `CHANGELOG.md`, `docs/roadmap.md`, `README.md`, `CLAUDE.md` (estado atual), `src/SecureGate/README.md`, `src/NotificationHub/README.md`, `src/AdminPortal/README.md`, `src/SDK/Secco.SDK.AspNetCore/README.md`, `.claude/skills/secco-platform-release/SKILL.md` (release de imagem e a visibilidade no GHCR)
- Create: `docs/guia-de-implantacao.md`

- [ ] **Step 1:** ADR-0062 "Distribuição dos serviços por imagem no GHCR". Contexto, decisão (registro, nomes, público, uma versão da plataforma, tags sem `latest`, varredura e smoke antes do push, SBOM e provenance, verbo `healthcheck`, proxy reverso com rede obrigatória, certificado fora de DEV/Testing, schema do Hangfire no `migrate`, imagem sem settings de dev), alternativas descartadas (seção "Abordagens" do design) e consequências (sessões do AdminPortal; o feed privado fora do build e quando passaria a entrar).
- [ ] **Step 2:** `docs/guia-de-implantacao.md` com os 13 itens da seção 8 do design. A tabela de variáveis sai das classes de options e dos validadores de cada produto (procurar `SectionKey`, `BindConfiguration` e `IValidateOptions`), nunca de memória. O compose é incluído por referência ao `deploy/docker-compose.yml`, com trechos.
- [ ] **Step 3:**
  - CHANGELOG: uma seção "Imagens da plataforma 0.1.0" com a tabela de compatibilidade; seções do `Secco.SDK.AspNetCore` (`healthcheck`, proxy reverso, sobrecarga do Hangfire), do SecureGate (certificado) e do NotificationHub (schema).
  - Notas de upgrade: Staging exige certificado; o compose de dev precisa do `build.args`; `Secco:ReverseProxy` exige rede.
- [ ] **Step 4:** roadmap, READMEs, `CLAUDE.md` e a skill de release.
- [ ] **Step 5:** revisar o diff inteiro da documentação contra o código (nenhum fato inventado) e fazer o commit `docs(platform): ADR-0062 e guia de implantação por imagem`.

### Task 12: verificação final

- [ ] **Step 1:** `dotnet build Secco.Platform.slnx -c Release` → `0 Warning(s)`, `0 Error(s)`.
- [ ] **Step 2:** `dotnet test Secco.Platform.slnx -c Release 2>&1 | tee test.log` e depois `grep -E "error|Failed" test.log` → nada além de nomes de teste. Confirmar que o AdminPortal.Tests rodou.
- [ ] **Step 3:** o `bash scripts/deploy-smoke.sh local 0.1.0` passa de novo depois de tudo.
- [ ] **Step 4:** checklist da skill `secco-platform-standards` (ADR-0020 incluída) e push da `main`. CI verde, inclusive `deploy-example` e `docker-stacks`.

### Task 13: release

- [ ] **Step 1:** seguir a skill `secco-platform-release`. Rodar `python3 scripts/check-release-chain.py --pendentes` e publicar as tags de pacote apontadas (no mínimo `sdk/v0.11.0`; `templates/v0.5.0` pelo template), **em levas de no máximo três tags por push**, na ordem de dependência. Esperar cada workflow ficar verde.
- [ ] **Step 2:** criar e empurrar `v0.1.0` sozinha. Acompanhar `publish-images.yml` até verde (`gh run watch`).
- [ ] **Step 3:** conferir no GHCR: os quatro pacotes ligados ao repositório e **públicos**. Se algum nascer privado, avisar o Rafael (trocar a visibilidade é passo manual dele na interface) antes de dar por concluído.
- [ ] **Step 4:** de uma máquina sem login no GHCR: `docker pull ghcr.io/rafsecco/secco-securegate:0.1.0` e `:0.1` → sucesso; `docker buildx imagetools inspect` mostra `linux/amd64` e `linux/arm64` e as attestations (SBOM e provenance).
- [ ] **Step 5:** marcar os checkboxes do roadmap e fazer o commit `docs(platform): imagens 0.1.0 publicadas`.
