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

## Autenticação recente e token trocado (ADR-0050)

Três peças para áreas que exigem a pessoa presente e para chamadas que precisam de um token curto e privilegiado (elevação do `logstream`, operador designado com `securegate:admin`). A plataforma não muda: o pacote só faz a parte da relying party.

**1. Exigir autenticação recente.** `RequireSeccoRecentAuthentication(maxAge, requireMfa: true)` confere o `auth_time` e o `amr` do **access token da sessão no cofre** (não as claims do cookie). Autenticação mais antiga que `maxAge`, ou sem `auth_time` (token opaco), desafia o esquema OIDC com `prompt=login` e `max_age`, voltando à mesma URL. Autenticação recente sem `mfa` responde `403` — desafiar de novo levaria uma conta sem segundo fator a um laço de login. O desafio é feito por um decorador do `IAuthorizationMiddlewareResultHandler` registrado por `AddSeccoRelyingPartyTokens()`; vale para MVC, Razor Pages e APIs mínimas. O decorador envolve o handler que já estiver registrado (o seu, ou o padrão), então chame `AddSeccoRelyingPartyTokens()` **depois** de registrar um handler próprio. O retorno é um `GET` à URL original (corpo de `POST` não é preservado).

**2. Token trocado por escopo.** `ISeccoExchangedTokenProvider.GetAsync(scope)` faz a troca RFC 8693 no token endpoint, com o access token atual da pessoa como `subject_token`, e guarda o resultado **no cofre da sessão** (nunca no cookie) até perto do vencimento (`RefreshMargin`). A renovação do token da pessoa preserva os tokens trocados. O resultado é `Issued`, `Refused` ou `NoSession`; a recusa (`400`/`401`) é genérica de propósito (ADR-0031, ADR-0049) e quem decide pedir reautenticação é a aplicação. Um escopo por chamada. Nada é renovado em segundo plano.

**3. Anexar o token trocado.** `AddSeccoExchangedToken(scope)` num `IHttpClientBuilder` anexa o token trocado; sem token lança `SeccoReauthenticationRequiredException` — nunca segue sem autorização nem com o token comum da pessoa. As ressalvas de escopo de DI são as do `SeccoUserTokenHandler` (Blazor Server: use o provedor no escopo do circuito).

Exemplo (MVC no estilo da Intranet): a área administrativa exige autenticação recente com segundo fator, e o client do SecureGate leva o token de operador.

```csharp
builder.Services.AddSeccoRelyingPartyTokens();

builder.Services.AddAuthorization(options =>
    options.AddPolicy("Operacao", policy => policy
        .RequireAuthenticatedUser()
        .RequireSeccoRecentAuthentication(TimeSpan.FromMinutes(5))));

builder.Services.AddHttpClient<ISecureGateClient, SecureGateClient>(c => c.BaseAddress = new Uri("https://securegate.interno"))
    .AddSeccoExchangedToken("securegate:admin");

[Authorize(Policy = "Operacao")]
public class OperacaoController(ISecureGateClient secureGate) : Controller
{
    public async Task<IActionResult> Tenants() => View(await secureGate.ListTenantsAsync());
}
```

Quando a troca é recusada mesmo com autenticação recente (a pessoa não é operador, por exemplo), o client lança `SeccoReauthenticationRequiredException`; trate-a no filtro de exceção da aplicação, redirecionando ou mostrando acesso negado. Para o token direto, sem `HttpClient`:

```csharp
var result = await exchanged.GetAsync("logstream", ct);
if (result.IsIssued) { /* result.AccessToken, result.ExpiresAt */ }
```

`ISeccoUserAuthentication.GetAuthenticationAsync()` expõe o instante e os métodos de autenticação da sessão (`AuthenticatedAt`, `Methods`), lidos do access token; token ilegível ou sem as claims dá "desconhecido" (fail-closed). `SeccoUserSession.ExchangedTokens` guarda os tokens trocados; o `ToString` dos tipos de token não os expõe.

## Blazor Server

O circuito não tem `HttpContext` depois da primeira renderização, e o `IHttpClientFactory` cria os handlers num escopo próprio. Num circuito:

1. registre uma `ISeccoUserContext` própria, sobre o `AuthenticationStateProvider`, **antes** de `AddSeccoRelyingPartyTokens()` (o registro padrão é `TryAdd`);
2. em vez do handler, injete `ISeccoUserTokenProvider` (escopo do circuito) e anexe o token ao `HttpClient` de cada chamada.

## Operação e segurança (ADR-0020)

- Tokens nunca vão ao cookie nem a log ou mensagem de exceção; `SeccoUserSession.ToString()` não os expõe.
- O cofre guarda **refresh tokens**. Com o provider em memória, reiniciar o processo leva todos a novo login. Com Redis, o Redis passa a guardar credencial e entra no backup e no controle de acesso com esse peso.
- Mais de uma instância da aplicação exige um `IDistributedCache` compartilhado (registre-o no host). A exclusão de renovação é por instância; o refresh já usado é recusado pelo emissor, então a renovação concorrente entre réplicas leva uma das sessões a novo login.
