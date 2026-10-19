# Secco.SDK.RelyingParty

Custódia do token da pessoa para aplicações **relying party** (cookie + OIDC authorization code/PKCE, ADR-0023), conforme a ADR-0046. Para chamar a plataforma on-behalf-of a pessoa logada, a aplicação precisa guardar e renovar o token dela; este pacote faz isso, em vez de cada aplicação copiar o código.

Pacote fino de propósito: não depende do `Secco.SDK.AspNetCore` (Hangfire) nem de nenhum `Secco.<Produto>.Client`. Usa o framework ASP.NET Core e o `Microsoft.AspNetCore.Authentication.OpenIdConnect`.

## O que faz

- **Cofre no servidor** sobre `IDistributedCache` (ADR-0035). No `OnTokenValidated`, access e refresh token vão para o cofre e **só o id da sessão** entra no cookie (claim `secco_session`). Cookie cuja sessão não está no cofre é rejeitado (`OnValidatePrincipal`) e a pessoa volta ao login.
- **Renovação sob demanda**: `ISeccoUserTokenProvider.GetAccessTokenAsync()` devolve um token válido; a menos de 60 s do vencimento ele é renovado com o refresh token e o refresh **rotacionado** substitui o anterior no cofre. Uma renovação por sessão por instância (semáforo em memória do processo).
- **Renovação recusada** (sessão revogada, ADR-0032) ou sessão ausente: a entrada é apagada do cofre e a pessoa é mandada para novo login (`ISeccoUserContext.RequestReauthentication()`); o provedor devolve `null`.
- **Escopos concedidos** do token atual: `GetGrantedScopesAsync()` / `HasScopeAsync("securegate:tenant-admin")`.
- **`SeccoUserTokenHandler`**: `DelegatingHandler` que anexa `Authorization: Bearer <token da pessoa>` a qualquer `HttpClient`.

## Uso

```csharp
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie()
    .AddOpenIdConnect(options =>
    {
        options.Authority = "https://securegate.interno";
        options.ClientId = "meu-app";
        options.ClientSecret = "...";
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = false;                  // o token vai para o cofre, nunca para o cookie
        options.Scope.Add("offline_access");         // sem refresh token não há renovação
    });

builder.Services.AddSeccoRelyingPartyTokens();       // cofre + eventos do cookie e do OIDC

// Client NSwag com o Bearer da pessoa
builder.Services.AddHttpClient<ISecureGateClient, SecureGateClient>(c => c.BaseAddress = new Uri("https://securegate.interno"))
    .AddSeccoUserToken();
```

Os eventos que a aplicação já definiu em `OnTokenValidated` e `OnValidatePrincipal` continuam valendo (são encadeados). Os esquemas, o nome da claim, a validade da sessão (padrão 8 h deslizantes) e a margem de renovação são ajustáveis em `SeccoRelyingPartyOptions`.

## Blazor Server

O circuito não tem `HttpContext` depois da primeira renderização, e o `IHttpClientFactory` cria os handlers num escopo próprio. Num circuito:

1. registre uma `ISeccoUserContext` própria, sobre o `AuthenticationStateProvider`, **antes** de `AddSeccoRelyingPartyTokens()` (o registro padrão é `TryAdd`);
2. em vez do handler, injete `ISeccoUserTokenProvider` (escopo do circuito) e anexe o token ao `HttpClient` de cada chamada.

## Operação e segurança (ADR-0020)

- Tokens nunca vão ao cookie nem a log ou mensagem de exceção; `SeccoUserSession.ToString()` não os expõe.
- O cofre guarda **refresh tokens**. Com o provider em memória, reiniciar o processo leva todos a novo login. Com Redis, o Redis passa a guardar credencial e entra no backup e no controle de acesso com esse peso.
- Mais de uma instância da aplicação exige um `IDistributedCache` compartilhado (registre-o no host). A exclusão de renovação é por instância; o refresh já usado é recusado pelo emissor, então a renovação concorrente entre réplicas leva uma das sessões a novo login.
