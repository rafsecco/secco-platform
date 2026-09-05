# Canais externos do NotificationHub (Teams, Slack) — design

**Data:** 2026-09-05
**Status:** aprovado para planejamento
**Item de origem:** issue [#13](https://github.com/rafsecco/secco-platform/issues/13) (`adopter-demand`) — decisão registrada na **ADR-0029**.

## Contexto

O conjunto de canais é fechado por decisão declarada no código: canal mapeia para caminho de código que precisa existir no produto. A demanda é alcançar Teams e Slack sem transformar o Hub num mecanismo genérico de webhooks.

### O que a validação encontrou

| Ferramenta | Situação verificada em 2026-09-05 |
| --- | --- |
| **Teams** | Office 365 Connectors **desativados entre 18 e 22 de maio de 2026**; webhooks de connector não funcionam mais. Caminho atual: **Power Automate Workflows**, gatilho *When a Teams webhook request is received*, payload Adaptive Card ou MessageCard, identidade do Flow bot |
| **Slack** | **Incoming webhooks seguem suportados**, sem aviso de descontinuação na documentação oficial. `chat.postMessage` é recomendado apenas para apagar/editar/rotear dinamicamente |

A conclusão prática: os dois **não** são o mesmo problema com URL diferente.

## Ordem de execução

São **dois PRs**, nesta ordem — o primeiro não depende da #13 e vale por si.

### PR 1 — promover o cifrador de segredos para o SDK

Hoje `IConnectionStringCipher` e `AesGcmConnectionStringCipher` são internos ao `Secco.SecureGate.Infrastructure`, e o `secco-intranet` já reimplementou o mesmo formato como `EnvelopeCipher`. O NotificationHub seria a terceira cópia.

- Nasce `ISeccoSecretCipher` + `AesGcmSecretCipher` em `Secco.SDK.EntityFrameworkCore` (é onde vive o value converter que o usa) ou em `Secco.SDK.AspNetCore` — decidir na implementação pelo que evita dependência nova; o formato `secco-enc:v1:` **não muda**.
- `Secco.SecureGate.Infrastructure` passa a consumir o do SDK; `IConnectionStringCipher` vira um alias fino ou é removido, sem alterar comportamento.
- Critério de aceite: os 192 testes do SecureGate passam **sem alteração**, incluindo os que asseveram `secco-enc:v1:` na coluna. Nenhuma mudança de dado.

### PR 2 — a issue #13

## Arquitetura

### Canais e configuração

`NotificationHubChannels.All` passa a `[Email, InApp, Teams, Slack]`. O conjunto segue fechado e validado.

Entidade nova no banco **do tenant**:

```csharp
public sealed class ChannelConfiguration : BaseEntity
{
    public string Channel { get; }        // ds_channel — teams | slack
    public string Destination { get; }    // ds_destination — URL, CIFRADA em repouso
    public bool Enabled { get; }          // fl_enabled
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; }
}
```

Par (tenant, canal) único — o tenant é implícito, é o banco. `Destination` usa o mesmo value converter da ADR-0025, agora vindo do SDK. Nunca volta em resposta: write-only, como a connection string do catálogo.

Gestão por endpoints do próprio Hub:

```
PUT    /api/v1/channel-configurations/{channel}   notification-channels:write   idempotente
GET    /api/v1/channel-configurations             notification-channels:read    sem o segredo
DELETE /api/v1/channel-configurations/{channel}   notification-channels:write
```

O `GET` devolve canal, habilitado e datas — **nunca** a URL. Permissões novas seguem o formato da ADR-0021.

### Entrega

`Notification` ganha `Channel` (`ie_channel`) e passa a representar entrega por canal externo, não "notificação por e-mail". `Recipient`/`Subject`/`Body` permanecem, com significado por canal: no Teams e no Slack o destino vem da configuração do tenant, e `Recipient` fica nulo.

O job de envio vira um por canal, selecionado pelo discriminador — mesma máquina, mesmo `Pending/Sent/Failed`, mesmo retry do Hangfire por entrega (ADR-0015). Nada disso é novo; é o caminho do e-mail estendido.

```
DispatchNotification (channels: ["teams"])
        ↓
Notification { Channel = Teams, Status = Pending }
        ↓  job com retry
ITeamsNotificationProvider → resolve destino na configuração do tenant → POST Adaptive Card
```

### Providers

Um por ferramenta, sem abstração genérica no meio:

| Tipo | Responsabilidade |
| --- | --- |
| `ITeamsNotificationProvider` | traduzir título/mensagem em Adaptive Card e postar na URL de Workflow |
| `ISlackNotificationProvider` | traduzir em `text`/Block Kit e postar na URL de incoming webhook |

Ambos com `HttpClient` nomeado, herdando a resiliência do SDK (`AddSeccoResilience`). A ADR-0006 proíbe `HttpClient` manual **entre produtos Secco** — Teams e Slack são terceiros, então não se aplica.

## Segurança (ADR-0020)

| Vetor | Tratamento |
| --- | --- |
| **SSRF** | A URL **nunca** vem do payload. Vem da configuração do tenant, gravada por endpoint com permissão própria. É a decisão central da ADR-0029 |
| **Segredo em repouso** | URL cifrada com `secco-enc:v1:`; write-only na API, nunca em resposta, nunca em log |
| **Isolamento de tenant** | Configuração vive no banco do tenant; não há caminho para ler a de outro |
| **Vazamento por erro** | Falha do provider externo vira classificação e status HTTP, nunca corpo da resposta — que pode ecoar o conteúdo da notificação |
| **Validação da URL configurada** | Aceitar só `https` e recusar host privado/loopback no momento do cadastro reduz o estrago de um operador enganado. É defesa em profundidade: a barreira principal continua sendo a URL não vir do consumidor |
| **DoS** | O envio é assíncrono e com retry do Hangfire, como o e-mail; o teto de lote da issue #15 já limita o disparo |

## Escopo

**Dentro:** os dois canais, a entidade e os endpoints de configuração, o discriminador na `Notification`, os dois providers, migrations nos dois engines, contrato e client regenerados, testes, ADR-0029 e documentação.

**Fora, registrado:** Bot/Teams App (identidade própria em vez de Flow bot); `chat.postMessage` do Slack (editar/apagar/rotear); Google Chat; tela no AdminPortal para configurar canais; e template de mensagem — o conteúdo continua pronto, vindo do chamador, como o produto sempre fez.

## Testes

Unit, sem rede:

- `Channels_All_ContainsTeamsAndSlack` e a recusa de canal desconhecido.
- `TeamsProvider_TranslatesToAdaptiveCard` / `SlackProvider_TranslatesToTextPayload` — a tradução é o que cada provider existe para fazer.
- `Provider_WhenToolRefuses_ThrowsWithoutLeakingResponseBody`.
- `ChannelConfiguration_RejectsNonHttpsAndPrivateHosts`.
- `Dispatch_WhenChannelHasNoConfiguration_FailsWithClearError` — o caso que mais vai acontecer na adoção.

Integração com `SeccoApiFactory`:

- `PutConfiguration_ThenGet_NeverReturnsTheDestination` + asserção direta na coluna provando `secco-enc:v1:`.
- `Dispatch_ToTeams_CreatesPendingNotificationWithChannelDiscriminator`.
- `Configuration_FromAnotherTenant_IsNeverVisible`.
- Paridade PostgreSQL da entidade nova.

O envio real não é testado contra Teams nem Slack: os providers dependem de `HttpClient` substituível, e o que se testa é tradução, tratamento de erro e não-vazamento.

## Riscos

- **A URL do Workflow tem dono.** Se a pessoa sair da organização, o fluxo pode ficar órfão e a entrega para em silêncio do lado do Hub (o POST continua respondendo). Mitigação é operacional — coproprietário — e entra no README, não no código.
- **Generalizar `Notification` mexe em tabela com dado existente.** A migration precisa dar `Channel = Email` às linhas atuais, para que o histórico continue significando o que significava.
