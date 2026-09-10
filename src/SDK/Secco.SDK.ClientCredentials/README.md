# Secco.SDK.ClientCredentials

Client credentials OAuth2 para clients de produto (`Secco.<Produto>.Client`) autenticarem chamadas de máquina a máquina contra o emissor da plataforma (o SecureGate). Extraído de `Secco.SDK.AspNetCore` para ser um pacote FINO: só `Microsoft.Extensions.Http` e DI/Configuration — nada de Hangfire, JwtBearer ou AspNetCore, que os clients de produto (inclusive um worker de console que só quer disparar uma notificação) não têm por que carregar.

Resolve a issue [#25](https://github.com/rafsecco/secco-platform/issues/25): antes, `AddLogStreamClient()`/`AddNotificationHubClient()` registravam o `HttpClient` só com a URL base — todo endpoint exige permissão (ADR-0021), então o client compilava e tomava 401 na primeira chamada. O adotante tinha que remontar a composição do `HttpClient` à mão.

## Uso (por um `Secco.<Produto>.Client`)

```csharp
services.AddSeccoClientCredentialsOptions("Secco:MeuProduto");

var tokenStore = new SeccoAccessTokenStore();

services.AddHttpClient<IMeuProdutoClient, MeuProdutoClient>(client =>
        client.BaseAddress = new Uri(baseUrl))
    .AddSeccoClientCredentials(
        scope: "meu-produto",
        tokenStore,
        resolve: sp => sp.GetRequiredKeyedService<SeccoClientCredentialsOptions>("Secco:MeuProduto"));
```

```jsonc
{
  "Secco": {
    "MeuProduto": {
      "BaseUrl": "https://meu-produto.interno"
      // ClientId e ClientSecret caem para a seção Secco:SecureGate quando não
      // declarados aqui — a maioria dos produtos já a configura.
    }
  }
}
```

## Comportamento

- **Ausência total de credenciais** (nenhuma das três chaves, nem por fallback): nenhum handler é anexado — o `HttpClient` segue sem autenticação. Serve DEV com um token HS256 emitido fora da plataforma.
- **Configuração PARCIAL**: `SeccoClientCredentialsOptions.Validate()` lança `InvalidOperationException` listando o que falta, citando a seção. Configuração ausente virando no-op transformaria erro de setup em `NullReferenceException` em runtime — por isso parcial é sempre fail-fast (ADR-0020).
- **Registro keyed por seção**: `AddSeccoClientCredentialsOptions` registra `SeccoClientCredentialsOptions` como serviço keyed pelo próprio `sectionKey`. Dois clients no mesmo host (LogStream e NotificationHub, por exemplo) chamam o método com seções diferentes sem colidir num singleton comum.
- **`ClientSecret` nunca aparece em log, mensagem de exceção ou `ToString`** (ADR-0020).

## O que este pacote não faz

- Não decide o scope solicitado — quem chama `AddSeccoClientCredentials` informa o scope do próprio recurso (least privilege, ADR-0020).
- Não compartilha `SeccoAccessTokenStore` entre recursos — cada consumidor mantém o próprio, porque pede um token com o próprio scope.
