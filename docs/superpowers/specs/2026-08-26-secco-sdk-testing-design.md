# Secco.SDK.Testing + generalização de provider — design

**Data:** 2026-08-26
**Status:** aprovado para planejamento
**Itens de origem:** os **dois** itens de backlog do `docs/roadmap.md` — "extrair uma base
reutilizável dos 4 `*ApiFactory` de testes de integração" e "generalizar
`DatabaseProviderConfigurator`/`BindSection`". Agrupados por decisão explícita: os dois tocam os
mesmos arquivos do template, e separá-los custaria duas passadas pelo mesmo `validate-template`.

## Contexto

Quatro projetos mantêm uma `WebApplicationFactory` de integração praticamente idêntica, e as
cópias já divergiram:

| Factory | Container | Token JWT | Permissões via configuração | Banco de plataforma | Serviço substituído |
| --- | --- | --- | --- | --- | --- |
| `LogStreamApiFactory` | MsSql | helper estático à parte, com `role` | sim (6 permissões) | — | — |
| `SecureGateApiFactory` | MsSql | helper estático à parte, com `scope`, sem `tenant_id` | — | `secco_securegate` | — |
| `NotificationHubApiFactory` | MsSql | embutido na factory, com `role` | sim (4 permissões) | Hangfire, via SQL cru | `FakeEmailSender` |
| `SampleServiceApiFactory` (template) | MsSql | embutido na factory, **sem `role`** | — | — | — |

Repetido literalmente nas quatro: as strings `chave-de-testes-com-32-caracteres!!`, `secco-tests`
e `test-admin`; o par `SemaphoreSlim` + flag `_migrated`; o método `GetTenantConnectionString`.

O template propaga uma quinta cópia a cada `dotnet new secco-service`, e a cópia que ele propaga
é justamente a defasada (token sem `role`) — ou seja, a divergência não é estável, ela cresce.
Pela ADR-0013, divergência entre template e o padrão é bug de prioridade alta.

## Decisões tomadas nesta rodada

1. **Pacote publicável desde já** (`IsPackable=true`, `MinVerTagPrefix` `sdk-testing/v`). Motivo:
   `Secco.Templates` é publicável, então um produto gerado fora do monorepo precisa da base por
   NuGet — senão o template gera um projeto de teste que não compila.
2. **Instância isolada por padrão, com override por variável de ambiente.** Sem
   `SECCO_TEST_SQLSERVER`, cada suíte sobe o próprio container, exatamente como hoje: o CI não
   muda e continua hermético. Com a variável preenchida, a base usa a instância apontada e não
   sobe container — é o caminho para máquinas onde N containers de SQL Server saturam o Docker.
   Descartado `WithReuse(true)`: o Ryuk não reapeia containers reusados, então o lixo se acumula
   na máquina do dev até limpeza manual.
3. **Escopo v1 = núcleo.** Factory base + geração de token + helpers de configuração + criação de
   banco de plataforma. Fora do v1: fixture de paridade Postgres (2 cópias hoje, decidido quando
   doer).
4. **Base abstrata sobre uma instância `internal`.** A API pública é um tipo; a lógica
   container-vs-env-var vive num `internal sealed class SeccoSqlServerInstance`, testável sem
   Docker. A superfície pública de um pacote sujeito a semver (ADR-0009) fica mínima.
5. **ADR-0027**, curta, complementando a ADR-0012.
6. **Seleção de provider generalizada sem acoplar o SDK a engine.** O item de backlog, como
   escrito, mandava levar o `DatabaseProviderConfigurator` para o `Secco.SDK.EntityFrameworkCore` —
   mas ele chama `UseSqlServer`/`UseNpgsql`, e aquele pacote é publicado declaradamente agnóstico
   de provider (comentário no csproj + a cláusula da ADR-0018 "a arquitetura permanece extensível a
   outros engines"). Adotado o desenho por receita: o produto fornece o que aplicar, o SDK
   seleciona. Zero dependência de provider adicionada ao pacote publicado.
7. **`BindSection` é apagado, não extraído** — contraria o item de backlog, deliberadamente. Ver a
   seção própria.

## Arquitetura

### Superfície pública

```csharp
namespace Secco.SDK.Testing;

public abstract class SeccoApiFactory<TProgram> : WebApplicationFactory<TProgram>, IAsyncLifetime
    where TProgram : class
{
    protected abstract string Audience { get; }
    protected abstract Task MigrateAsync(IServiceProvider services);

    protected virtual void ConfigureTestConfiguration(IDictionary<string, string?> settings) { }
    protected virtual void ConfigureTestServices(IServiceCollection services) { }
    protected virtual Task OnInitializedAsync() => Task.CompletedTask;

    public string GetConnectionStringFor(string databaseName);
    public Task CreateDatabaseAsync(string databaseName, CancellationToken ct = default);
    public Task EnsureDatabaseMigratedAsync();

    public string CreateToken(Guid tenantId, string subject = "test-user", string role = "test-admin");
    public string CreateTokenWithScopes(params string[] scopes);

    protected static void AddTenant(IDictionary<string, string?> settings, Guid tenantId, string connectionString);
    protected static void AddRolePermissions(IDictionary<string, string?> settings, string role, params string[] permissions);
}
```

`ConfigureWebHost` é `sealed` na base: ela fixa o ambiente `Testing`, monta o dicionário de
configuração base e chama os hooks. Selar é o que impede o drift de voltar por dentro. A escotilha
legítima continua sendo herdar da factory do produto e sobrescrever os hooks — é o que a variante
de auth do SecureGate faz.

`Audience` e `MigrateAsync` são `abstract` de propósito: esquecer de definí-los vira erro de
compilação, não teste vermelho no CI.

### Dois métodos de token, não um genérico

Os formatos em uso são três: `tenant_id` + `role` + `sub`; `tenant_id` + `sub`; e `sub` + `scope`
sem tenant. Um método único com todos os parâmetros opcionais produziria chamadas como
`CreateToken(null, "x", null, "a", "b")` nos testes. Dois nomes distintos seguem o precedente que
o próprio SecureGate já registrou em `CreateClientAsync` / `CreateClientWithRolesAsync` ("nome
distinto por design: um overload posicional confundiria scope com roles").

Custo aceito: se um dia surgir a necessidade de tenant **e** scope no mesmo token, entra um
terceiro método.

### `AddRolePermissions`

Hoje o LogStream digita `Secco:Authorization:Roles:test-admin:Permissions:0` até `:5` à mão.
Inserir uma permissão no meio da lista gera chave duplicada e a última vence — falha silenciosa.
O helper emite os índices.

### `SeccoSqlServerInstance` (internal)

Na construção decide o modo:

- `SECCO_TEST_SQLSERVER` preenchida → usa aquela instância como servidor base; não sobe container.
- ausente ou vazia → `new MsSqlBuilder().Build()` + `StartAsync()`, como hoje.

**Sufixo de execução, sempre.** `GetConnectionStringFor("secco_logstream_alfa")` resolve para
`secco_logstream_alfa_<8 hex aleatórios>`, sorteados por instância de factory. Obrigatório no modo
externo, onde duas suítes concorrentes colidiriam no mesmo servidor; mantido também no modo
container por uniformidade — um caminho de código só, e imune caso alguém habilite reuse depois.
Nenhum teste atual afirma sobre nome de database, então a mudança é segura.

**Restrição que o sufixo impõe, verificada:** duas instâncias de factory sorteiam sufixos
diferentes, logo não compartilham database. Isso só quebraria um teste que passasse o nome de um
banco entre instâncias — e não existe: todos os chamadores de `GetConnectionStringFor` nos testes
atuais (`FoundationTests`, `LogRetentionTests`, `CatalogE2ETests`, `CrossProductTokenFlowTests`,
`ConnectionStringEncryptionTests`, `PlatformSchemaTests`) resolvem a string a partir da mesma
instância que estão exercitando. Quem implementar deve manter essa propriedade ao migrar.

**Descarte.** No modo container, o container morre e leva tudo — comportamento atual. No modo
externo, `DROP DATABASE IF EXISTS` best-effort de cada nome entregue, precedido de
`SET SINGLE_USER WITH ROLLBACK IMMEDIATE`, cada um em `try/catch`: falha de limpeza **nunca**
derruba a suíte. Sem isso o servidor externo acumularia dezenas de bancos por dia de trabalho.

## Segurança (ADR-0020)

1. **Chave HS256 aleatória por instância** — 32 bytes de `RandomNumberGenerator`, nunca constante.
   É o ganho central da entrega. Um pacote publicado no nuget.org com
   `chave-de-testes-com-32-caracteres!!` embutida seria uma chave de assinatura de conhecimento
   público, e `SeccoAuthenticationOptionsValidator` só barra `DevelopmentSigningKey` em Production
   (valida presença, exclusividade com Authority e comprimento — não tem como saber que uma chave
   é conhecida). Staging ou homologação de adotante que reaproveitasse a constante aceitaria token
   forjado por qualquer pessoa que lesse o pacote. Consequência mecânica: os dois
   `JwtTestTokenFactory` estáticos deixam de existir, e as 12 chamadas passam a sair da instância
   da factory.
2. **`<DevelopmentDependency>true</DevelopmentDependency>`** — o pacote não flui transitivamente
   para quem referenciar o produto.
3. **Nome de database validado por allowlist** `^[a-z][a-z0-9_]{0,90}$` antes de entrar em
   `CREATE`/`DROP DATABASE`. Esses comandos não aceitam parametrização, então a interpolação é
   inevitável; a validação e o uso de colchetes são a mitigação correta. Hoje o NotificationHub
   interpola sem validação nenhuma, e é essa forma que o template propagaria se ganhasse background
   jobs. Registro honesto do risco: **não há input externo em lugar nenhum** deste caminho — o
   nome-base é `const` do próprio teste e o sufixo é hex gerado por nós. A correção é de forma e do
   que o padrão ensina, não de vulnerabilidade explorável.
4. **`SECCO_TEST_SQLSERVER` carrega senha** — nunca logada; mensagens de erro citam apenas o nome
   da variável, jamais o valor.
5. **Isolamento de tenant** — inalterado: a base só monta as chaves de configuração de tenancy que
   as factories já montavam; nenhuma lógica de resolução de tenant vive aqui.

## Migração dos consumidores

Tudo no mesmo PR. Deixar qualquer cópia de pé reintroduz o drift que motivou a entrega.

| Consumidor | Natureza | Notas |
| --- | --- | --- |
| `LogStreamApiFactory` + seu `JwtTestTokenFactory` | mecânica | helper estático deletado; chamadas passam à instância |
| `NotificationHubApiFactory` | mecânica | `OnInitializedAsync` → `CreateDatabaseAsync`; `FakeEmailSender` via `ConfigureTestServices`; a `ICollectionFixture` do Hangfire fica intocada |
| `SampleServiceApiFactory` (template) | mecânica + correção | o token gerado ganha `role`, que hoje falta |
| `SecureGateApiFactory` + seu `JwtTestTokenFactory` | mecânica | os três `Create*ClientAsync` são específicos de OIDC e ficam no produto |
| `SelfIssuedAuthSecureGateApiFactory` | **não-mecânica** | ver abaixo |

**O caso não-mecânico.** Essa variante sobrescreve `ConfigureWebHost` e chama `base.` — método que
passa a ser `sealed`. Ela migra para os dois hooks: `ConfigureTestConfiguration` para a Authority e
`ConfigureTestServices` para o backchannel lazy. A migração é uma melhoria, não só uma adaptação:
hoje ela apaga a chave da base escrevendo `""` e dependendo de "fontes posteriores vencem"; com o
dicionário na mão, faz `settings.Remove("Secco:Authentication:DevelopmentSigningKey")` — intenção
explícita no lugar de truque de precedência.

**Não migrar:** `LogStreamHost`, a classe aninhada dentro de `CrossProductTokenFlowTests`. Ela é um
`WebApplicationFactory` cru de propósito — não tem container próprio, empresta a connection string
da instância do SecureGate e aponta a Authority para o SecureGate real em vez de usar chave HS256.
Herdar da base lhe daria um container que ela não quer. Fica como está.

## Empacotamento e CI

- `MinVerTagPrefix` `sdk-testing/v`, com a entrada correspondente no mapa de tags de
  `.github/workflows/publish-packages.yml` (padrão de tag + caminho do csproj).
- `README.md`, `Description`, `PackageTags`, `DevelopmentDependency`.
- Dependências: `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.AspNetCore.TestHost`,
  `Testcontainers.MsSql`, `Microsoft.Data.SqlClient`, `Microsoft.IdentityModel.JsonWebTokens`,
  `xunit`; `ProjectReference` para `Secco.SharedKernel` (por `SeccoClaims`).
- **Sem job novo no CI**: o filtro `platform` já cobre `src/SDK/**` e `tests/SDK/**`, builda a
  solution inteira e dispara `validate-template`. Basta registrar os dois projetos na
  `Secco.Platform.slnx`.
- O template continua monorepo-first: `ProjectReference` relativo
  `..\..\..\SDK\Secco.SDK.Testing\Secco.SDK.Testing.csproj`, no mesmo padrão que o Infrastructure
  do template já usa para os outros dois SDKs.

## Generalização da seleção de provider

Duplicação medida: ~230 linhas nos quatro `*DatabaseOptions.cs`, em quatro blocos — o enum
(4 × 12 linhas, idênticos), o switch (4 × 10, idênticos exceto os nomes dos assemblies de
migrations), o `CreateOptions<TContext>` (4 × 8, idênticos exceto o tipo do contexto) e o
`BindSection` (4 × 6, idênticos — o SecureGate nem chegou a extrair, tem as duas linhas inline).

### Seletor por receita, no `Secco.SDK.EntityFrameworkCore`

```csharp
public sealed record SeccoDatabaseProviderRegistration(
    string Name,
    Action<DbContextOptionsBuilder, string> Apply);   // (builder, connectionString)

public static class SeccoDatabaseProviders
{
    public static void Configure(DbContextOptionsBuilder builder, string providerName,
        string connectionString, params SeccoDatabaseProviderRegistration[] registrations);

    public static DbContextOptions<TContext> CreateOptions<TContext>(string providerName,
        string connectionString, params SeccoDatabaseProviderRegistration[] registrations)
        where TContext : DbContext;
}
```

O nome do assembly de migrations fica **dentro** do `Apply`, não como campo do record: assim o SDK
não sabe nem que migrations existem, só aplica a receita recebida. Cada produto declara duas
linhas:

```csharp
new("SqlServer",  (b, cs) => b.UseSqlServer(cs, o => o.MigrationsAssembly("Secco.LogStream.Migrations.SqlServer"))),
new("PostgreSql", (b, cs) => b.UseNpgsql(cs,     o => o.MigrationsAssembly("Secco.LogStream.Migrations.Postgres"))),
```

**Cada produto mantém o próprio enum.** Ele é API legítima do produto e o bind de configuração já
valida o valor de graça; `Provider.ToString()` alimenta o seletor. O fail-fast do SDK — nome
desconhecido gera exceção citando o valor recebido e os aceitos, comparação ordinal ignore-case — é
rede secundária, não a primária.

`SecureGateDatabaseProviderConfigurator` continua existindo e continua `public` (as fábricas de
design-time dependem dele), mas encolhe para delegar ao seletor e acrescentar sua linha de
`UseOpenIddict` — a única variação real entre os quatro. Resultado: ~35 linhas viram ~10 por
produto, sem dependência nova no pacote publicado e sem reescrever a promessa de agnosticismo do
csproj.

**Segurança (ADR-0020):** `providerName` vem de configuração, ou seja, do operador do deployment e
não de usuário final. A mensagem de fail-fast cita o valor recebido e a lista de aceitos — nenhum
deles é segredo — e **nunca** a connection string.

### `BindSection`: apagar, não extrair

Esta parte contraria o item de backlog de propósito. `BindSection` é um
`IConfiguration.GetSection().Bind()` embrulhado num `AddSingleton(sp => ...)` para ser lazy, e o
framework já tem exatamente isso em `services.AddOptions<T>().BindConfiguration(key)`: resolve o
`IConfiguration` do container e portanto respeita fontes adicionadas tarde pelos testes, que é o
motivo declarado do helper existir.

O SecureGate já usa a forma nativa em `SecureGateInfrastructureExtensions`, com `.ValidateOnStart()`
em cima. O repositório tem as duas formas convivendo e a caseira é a pior das duas — promovê-la a
API pública de um pacote publicável seria consagrar o erro.

Então as 8 chamadas migram para `AddOptions<T>().BindConfiguration(...)` e o helper desaparece dos
quatro arquivos. Nenhuma API nova no SDK. Consumidores passam de
`GetRequiredService<LogStreamDatabaseOptions>()` para `IOptions<LogStreamDatabaseOptions>.Value`
(~10 pontos, mecânico) e ganham acesso ao `IValidateOptions<T>` + `ValidateOnStart()` que a
plataforma já usa em autenticação e no catálogo do SecureGate.

## ADR-0027

Curta, complementando a ADR-0012 (que decidiu a *estratégia* de testes mas não disse de onde vem a
*infraestrutura* de teste). Registra:

- a base de teste de integração da plataforma vive no `Secco.SDK.Testing`; produto novo herda dela
  e não escreve a própria `ApiFactory`;
- instância isolada por padrão, override por `SECCO_TEST_SQLSERVER`;
- chave de assinatura aleatória por instância, nunca constante em pacote publicado;
- **consequência**: o pacote depende de `xunit` (por `IAsyncLifetime`), então quem adota a base
  adota o xUnit. Coerente com a ADR-0012, que já fixa xUnit em toda a plataforma, mas isso passa de
  convenção interna a dependência contratual de um pacote público — precisa estar escrito;
- amarração com a ADR-0013: o template é atualizado na mesma entrega;
- **a seleção de provider é por receita**: o `Secco.SDK.EntityFrameworkCore` permanece agnóstico de
  engine (só `Relational`), e um terceiro engine se adiciona no produto, sem tocar no SDK — o que
  torna operacional a cláusula da ADR-0018 sobre extensibilidade, que até aqui era só intenção.

## Testes dos pacotes

`tests/SDK/Secco.SDK.Testing.Tests`, tudo sem Docker:

- escolha de modo (container vs `SECCO_TEST_SQLSERVER`);
- rejeição de nome de database fora da allowlist;
- unicidade do sufixo entre duas instâncias;
- **duas instâncias geram chaves de assinatura diferentes** — é o teste que trava a regressão de
  alguém reintroduzir uma constante;
- `AddRolePermissions` emitindo os índices corretos, inclusive além de 9;
- claims corretas em cada um dos dois métodos de token.

O seletor de provider ganha os seus no projeto que já existe, `tests/SDK/Secco.SDK.EntityFrameworkCore.Tests`,
também sem Docker: seleção correta por nome, ignore-case, e a exceção de nome desconhecido citando
o valor recebido e os aceitos.

## Ordem de execução

Três blocos independentes entre si, verificados um a um com a suíte verde antes de seguir. Não é
um PR monolítico: é um PR com três pontos de verificação internos.

1. **`Secco.SDK.Testing` + os 5 consumidores de teste.** Fecha quando os 493 testes existentes
   passam sem nenhuma `*ApiFactory` duplicada de pé.
2. **Seletor de provider + os 4 produtos.** Fecha quando os 4 `*DatabaseOptions.cs` estão
   encolhidos e a paridade Postgres do LogStream e do NotificationHub segue verde — é ela que
   prova que a receita do Npgsql continua sendo aplicada.
3. **Migração de options + os ~10 pontos de injeção.** Fecha quando `BindSection` não existe mais
   em nenhum dos quatro arquivos.

## Critério de conclusão

- os 493 testes existentes verdes;
- os testes novos do `Secco.SDK.Testing` e do seletor de provider verdes;
- `validate-template` verde;
- `dotnet build Secco.Platform.slnx --configuration Release` sem avisos (warnings são erros);
- `docs/roadmap.md` com **os dois** itens de backlog marcados.

## Fora de escopo

- Fixture de paridade Postgres no `Secco.SDK.Testing` (2 cópias hoje; entra quando doer).
- Trocar o enum de provider de cada produto por um enum compartilhado — avaliado e recusado nesta
  rodada: o enum é API legítima do produto e dá validação de configuração tipada de graça.
