# Começando — adotando a Secco Platform

Guia para **consumir** a plataforma: instalar os pacotes, subir um produto sobre o SDK, gerar um serviço novo pelo template e falar com os outros produtos pelos clients gerados. Para a visão de arquitetura, veja [architecture-overview.md](architecture-overview.md); para as decisões, as [ADRs](adr/secco-platform-adrs.md).

## 1. Configurar o feed (GitHub Packages)

Os pacotes `Secco.*` são publicados no **GitHub Packages** (ADR-0011). Adicione o feed ao `nuget.config` do seu repositório, restringindo `Secco.*` a ele (supply chain — o resto vem do nuget.org):

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="secco" value="https://nuget.pkg.github.com/rafsecco/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
    <packageSource key="secco"><package pattern="Secco.*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

O GitHub Packages exige autenticação mesmo para leitura. Use um **Personal Access Token (classic)** com o escopo `read:packages`:

```bash
dotnet nuget add source "https://nuget.pkg.github.com/rafsecco/index.json" \
  --name secco --username <seu-usuario> --password <PAT> --store-password-in-clear-text
```

> Em CI, prefira injetar o token por variável de ambiente / secret em vez de commitá-lo.

## 2. Instalar os pacotes

Versões publicadas atuais:

| Pacote | Versão | Para quê |
|---|---|---|
| `Secco.SharedKernel` | 0.4.6 | `Result<T>`, paginação, entidades base, claims/permissions |
| `Secco.SDK.AspNetCore` | 0.8.6 | Cross-cutting de runtime (auth, tenancy, correlation, health, resiliência, autorização, OpenAPI, background jobs) |
| `Secco.SDK.EntityFrameworkCore` | 0.4.0 | `SeccoDbContext`, nomenclatura de banco por convention (ADR-0017) e cifragem de segredo em repouso (ADR-0025) |
| `Secco.SDK.Testing` | 0.1.0 | Base das factories de teste de integração (ADR-0027); marcado `DevelopmentDependency` |
| `Secco.SecureGate.Client` | 0.14.0 | Client do SecureGate + `ITenantCatalog`/`IPermissionResolver` prontos |
| `Secco.LogStream.Client` | 0.4.0 | Client do LogStream |
| `Secco.SDK.ClientCredentials` | 0.1.10 | Client credentials OAuth 2 entre produtos; os clients de produto o usam sozinhos |
| `Secco.SDK.Logging` | 0.2.0 | Sink `ILogger` → LogStream (`AddLogStream()`, ADR-0008) |
| `Secco.SDK.Email` | 0.1.0 | Envio de e-mail (`ISeccoEmailSender`, SMTP/SendGrid por configuração, ADR-0033) |
| `Secco.NotificationHub.Client` | 0.6.0 | Client do NotificationHub |
| `Secco.Templates` | 0.2.0 | `dotnet new secco-service` |

> **`Secco.SDK.Logging` 0.1.0 não serve mais.** Ela foi empacotada quando o `Secco.LogStream.Client` ainda era 0.2.0 e chama um método que a 0.3.0 renomeou. Com os dois pacotes na versão mais recente, o despacho do lote falha e o dispatcher engole a exceção por design (ADR-0008) — o sintoma é log que não chega, sem nada cair. Suba para a 0.1.1.

```bash
dotnet add package Secco.SDK.AspNetCore
dotnet add package Secco.SDK.EntityFrameworkCore
```

## 3. Subir uma API sobre o SDK

O SDK compõe todo o cross-cutting da plataforma numa chamada (ADR-0004):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSeccoPlatform();   // correlation + auth + tenancy + health + resilience + authorization
builder.Services.AddSeccoOpenApi();    // OpenAPI com as convenções da plataforma (ADR-0006)

var app = builder.Build();

app.UseSeccoPlatform();                // ordem fixa: correlation → auth → tenancy
app.MapSeccoPlatform();                // health checks + defaults
app.MapOpenApi().AllowAnonymous();

await app.RunAsync();
```

Configuração mínima (`appsettings.json`) — autenticação valida os JWT emitidos pelo SecureGate via Authority/JWKS (ADR-0007); em DEV, uma chave HS256 é permitida:

```jsonc
{
  "Secco": {
    "Authentication": {
      "Audience": "secco-meuproduto",
      "Authority": "https://securegate.interno"    // ou, em DEV: "DevelopmentSigningKey": "<32+ chars>" + "Issuer"
    }
  }
}
```

Persistência com a nomenclatura de banco automática (ADR-0017):

```csharp
public sealed class MeuDbContext(DbContextOptions<MeuDbContext> options) : SeccoDbContext(options)
{
    public DbSet<MinhaEntidade> MinhasEntidades { get; set; }   // → tb_minhas_entidades, colunas ds_/dt_/fl_/...
}
```

## 4. Gerar um produto novo pelo template

A forma canônica de criar um produto (nunca copiar outro à mão):

```bash
dotnet new install Secco.Templates
dotnet new secco-service -n Secco.MeuProduto
```

O template gera as 4 camadas (ADR-0002), SDK plugado, OpenAPI + Scalar, client NSwag, migrations por engine (SQL Server + PostgreSQL), testes (unit + Testcontainers + contrato) e Docker — com um recurso **Sample** completo e apagável como referência executável.

## 5. Falar com os outros produtos (clients NSwag)

Comunicação entre produtos é **exclusivamente** pelos clients gerados (ADR-0006) — `HttpClient` manual é proibido. O `Secco.SecureGate.Client` ainda entrega implementações prontas para os produtos:

```csharp
builder.Services.AddSeccoPlatform();

// Catálogo central de tenants (ADR-0005) e resolução de permissões (ADR-0021),
// on-behalf-of via client credentials — least privilege por scope
builder.Services.AddSecureGateTenantCatalog();
builder.Services.AddSecureGatePermissionResolver();
```

```jsonc
{
  "Secco": {
    "SecureGate": {
      "BaseUrl": "https://securegate.interno",
      "ClientId": "meuproduto-service",
      "ClientSecret": "<segredo>",
      "Product": "meuproduto"
    }
  }
}
```

Sem a seção `Secco:SecureGate`, o produto segue no catálogo por configuração do SDK (DEV) — o comportamento local não muda. Autorização por permissão nos endpoints:

```csharp
group.MapGet("/recursos", Handler)
    .RequireAuthorization(MeuProdutoPermissions.Recursos.Read);   // "recursos:read" — resolvido em runtime, fail-closed
```

## 6. Registrar os clients OAuth no SecureGate

O `ClientId`/`ClientSecret` acima precisa existir no SecureGate. Há dois tipos de client, e cada um nasce por um caminho só (ADR-0037).

**Client de plataforma: sem tenant, declarado na configuração do SecureGate.** São os serviços da instalação: um produto que lê o catálogo ou resolve permissões (como o `meuproduto-service` acima), o AdminPortal, a identidade de auditoria. O token sai **sem** `tenant_id`, e o tenant alvo viaja no `X-Tenant-Id`. Declare cada um em `SecureGate:PlatformClients`, no SecureGate:

```jsonc
{
  "SecureGate": {
    "PlatformClients": [
      {
        "ClientId": "meuproduto-service",
        "Type": "ClientCredentials",                 // ou "AuthorizationCode" (+ RedirectUris, PKCE obrigatório)
        "Scopes": [ "catalog:meuproduto", "authorization:read", "logstream" ],
        "Roles": [ "meuproduto-service" ]
      }
    ]
  }
}
```

O `ClientSecret` vem de variável de ambiente (`SecureGate__PlatformClients__0__ClientSecret`) ou de cofre, nunca de arquivo versionado, e precisa de ao menos 32 caracteres fora de Development. A lista é a fonte da verdade: ela é reconciliada pelo **seed de referência**, que cria, atualiza e **remove** client de plataforma que saiu dela. Configuração inválida derruba o startup. Fora de Development, o seed de referência ainda não roda sozinho — ver [#34](https://github.com/rafsecco/secco-platform/issues/34).

**Client de produto: vinculado a um tenant, registrado pela API.** É a credencial de um sistema da empresa que **consome** os produtos da plataforma, por exemplo um "Sistema de compras" que grava log. Com escopo `securegate:admin`, pelo `Secco.SecureGate.Client`:

```csharp
var created = await secureGate.CreateProductClientAsync(tenantId, new ProductClientRequest
{
    Name = "Sistema de compras",
    Scopes = ["logstream"],          // só escopos de API de produto: logstream, notificationhub
    Roles = ["compras-writer"],      // perfis que já existem no tenant
});
// created.ClientId (cli_…) e created.ClientSecret — o secret aparece só aqui e na rotação
```

O token desse client sai **com** `tenant_id`. Um `X-Tenant-Id` de outro tenant é recusado com `400` pelo SDK, então o secret pode ser entregue a outro time sem que ele alcance outro tenant. Os escopos de infraestrutura (`securegate:admin`, `authorization:read`, `catalog:*`) não são concedíveis por esta via. Um sistema que precise deles é serviço de plataforma e entra pela configuração. Para trocar o secret use `RotateProductClientSecretAsync`, que invalida o anterior na hora; para revogar, `DeleteProductClientAsync`.

## Próximos passos

- **Arquitetura**: [architecture-overview.md](architecture-overview.md) — os pilares e como os produtos se encaixam.
- **Decisões**: [ADRs](adr/secco-platform-adrs.md) — a fonte da verdade (nenhum código as contradiz).
- **Roadmap**: [roadmap.md](roadmap.md).
- **Por produto**: os READMEs em [`src/`](../src) e [`templates/`](../templates).
