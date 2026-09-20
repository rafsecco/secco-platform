# Secco.SDK.Email

Envio de e-mail da Secco Platform (ADR-0033): porta `ISeccoEmailSender` com dois adaptadores — SMTP (MailKit) e SendGrid — selecionáveis por configuração. Pacote fino de propósito: nenhuma dependência de produto nem do `Secco.SharedKernel`, só os SDKs de e-mail em si e as abstrações de Options/DI do próprio framework.

Promovido de `Secco.NotificationHub.Infrastructure/Email/` (Fase 8) para o SDK: era exatamente a mesma necessidade do SecureGate (convite, redefinição de senha) — promoção sem mudança de comportamento, só a chave de seção virou parâmetro.

## Uso

```csharp
services.AddSeccoEmail("SecureGate:Email");
```

```jsonc
{
  "SecureGate": {
    "Email": {
      "Provider": "Smtp",       // ou "SendGrid"
      "Host": "localhost",      // exigido quando Provider = Smtp
      "Port": 587,
      "UseStartTls": true,
      "Username": null,
      "Password": null,
      "ApiKey": null,           // exigido quando Provider = SendGrid
      "FromAddress": "no-reply@secco.local",
      "FromName": "Secco"
    }
  }
}
```

A **chave da seção é do produto que consome o pacote** — cada produto usa a própria (`SecureGate:Email`, `NotificationHub:Email`, ...); o pacote não assume nenhuma.

## Comportamento

- **Validação fail-fast no startup** (ADR-0020): configuração pela metade falha em `ValidateOnStart()`, citando a chave da seção — nunca no primeiro envio, já com efeito represado.
- **Mensagem de erro cita só NOMES de chave, nunca valores** — segredo (senha, chave de API) nunca aparece em mensagem de exceção, log ou nome de teste.
- **Seleção de provider na composição**: `Provider = Smtp` (default) resolve `SeccoSmtpEmailSender`; `Provider = SendGrid` resolve `SeccoSendGridEmailSender`. O `ISendGridClient` só é construído quando referenciado — no caminho SMTP, a chave de API não precisa existir.
- Falha de envio **lança**; quem decide o retry é o chamador (job, handler) — a porta não engole exceção.

## O que este pacote não faz

- Não decide layout nem conteúdo do e-mail — isso é do chamador.
- Não persiste nem enfileira nada — é só a porta de envio; fila/retry é responsabilidade de quem chama (ex.: `IBackgroundJobScheduler` do `Secco.SDK.AspNetCore`).
