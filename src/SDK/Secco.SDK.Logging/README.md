# Secco.SDK.Logging

Sink `ILogger` → `Secco.LogStream` da Secco Platform (ADR-0008): `AddLogStream()` deixa qualquer produto escrever com `ILogger<T>` normalmente e ter os logs enviados ao LogStream com tenant, correlação e nome do serviço enriquecidos automaticamente — batch + fila local + retry, sem nunca bloquear o request. Resolve a issue [#1](https://github.com/rafsecco/secco-platform/issues/1), que travava o item de logging do roadmap do `secco-intranet`.

> **Status: esqueleto.** Este pacote ainda não tem `AddLogStream()`, provider, fila nem dispatcher — só a superfície de opções (`LogStreamLoggerOptions`) que o desenho final vai consumir. Ver `docs/superpowers/specs/2026-09-03-sdk-logging-logstream-sink-design.md` para a arquitetura completa planejada.

## Nota operacional — permissão em cada tenant

O client OIDC do produto precisa de um role com `log-entries:write` **em cada tenant** para o qual ele loga (a resolução de permissões é por `(tenant_id, role)`, ADR-0021). Sem essa permissão o LogStream responde 403 e o lote inteiro é descartado — silenciosamente do ponto de vista do produto (o sink nunca lança), visível apenas pelo contador de descartes, não por exceção.
