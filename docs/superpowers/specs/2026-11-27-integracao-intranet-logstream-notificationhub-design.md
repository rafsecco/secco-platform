# Integração da Intranet com LogStream e NotificationHub — design curto

**Data:** 2026-11-27
**Origem:** lacuna achada ao escrever o guia de implantação por imagens (ADR-0062).

## O problema

A Intranet chama o LogStream (trilha de auditoria e envio de log) e o NotificationHub (notificações) com o client **de plataforma** `intranet`. Duas coisas quebram:

1. **Tenant.** O token desse client não tem `tenant_id`, e os clients NSwag não mandam `X-Tenant-Id`. Os serviços não sabem de que tenant é a chamada.
2. **Permissão.**
   - O client não tem perfil.
   - O LogStream resolve permissão por perfil no SecureGate.
   - O NotificationHub resolve permissão e tenant só pela configuração local dele. Isso contraria a regra da plataforma (ADR-0021: permissões resolvidas pelo SecureGate; ADR-0005: catálogo de tenants central).

## Decisão (escolhida com o Rafael)

**A Intranet passa a usar um client de produto do tenant dela para os dois serviços, e o NotificationHub passa a ler tenants e permissões do SecureGate, como o LogStream.**

- **Client de produto** (ADR-0037), por exemplo `intranet-integracao`:
  - escopos `logstream` e `notificationhub`;
  - perfil `intranet-integracao` no tenant da Intranet;
  - o token sai com `tenant_id`, e os serviços resolvem o tenant pela claim, sem cabeçalho.
- **Configuração na Intranet, sem código novo.** O SDK aceita credenciais próprias por serviço: `Secco__LogStream__ClientId`/`ClientSecret` e `Secco__NotificationHub__ClientId`/`ClientSecret`. A URL do SecureGate cai na seção `Secco:SecureGate`.
- **Permissões do perfil**, no SecureGate:
  - LogStream: `audit-entries:write` (trilha) e `log-entries:write` (o sink do `Secco.SDK.Logging`);
  - NotificationHub: `notifications:write`, `notifications:read`, `in-app-notifications:read` e `in-app-notifications:write`.
  - Todas já estão nos catálogos publicados pelos dois produtos (ADR-0039).
- **O client de plataforma `intranet`** perde os escopos `logstream` e `notificationhub`, que deixam de ser usados (least privilege).

## Abordagens consideradas

- **Alinhar o NotificationHub ao SecureGate (escolhida).**
  - Um lugar só para o perfil e as permissões, e revogar vale para os dois serviços.
  - O banco de tenant do NotificationHub passa a vir do catálogo (ou do provisionamento, ADR-0028).
  - Some a limitação 4 do guia de implantação.
  - Quebra quem configurava tenants e perfis no próprio NotificationHub; hoje só a Intranet o usa (quebra aceitável no adotante, com nota).
- **Só configuração:** o perfil e a connection string ficariam repetidos nas variáveis do NotificationHub. A permissão viveria em dois lugares, e revogar no SecureGate não valeria nele. Descartada.
- **Client de plataforma com `X-Tenant-Id` por chamada:** exigiria código na Intranet (handler do cabeçalho). Mesmo assim, o perfil teria de existir em cada tenant. E um secret sem tenant alcançaria qualquer tenant pelo cabeçalho. O client de produto contém o secret no tenant dele. Descartada.

## O que muda

**Plataforma**
- NotificationHub (`Program.cs`): `AddSecureGateTenantCatalog()` e `AddSecureGatePermissionResolver()`, com a mesma decisão lazy do LogStream. Com a seção `Secco:SecureGate`, vale o SecureGate; sem ela (DEV e testes), vale a configuração.
- SecureGate (seed de referência): registra o escopo `catalog:notificationhub`, ao lado do `catalog:logstream`.
- Compose de implantação: o `notificationhub-service` ganha `catalog:notificationhub` e `authorization:read`.
- Smoke (`scripts/deploy-smoke.sh`), com a mesma chamada que a Intranet faz:
  1. cria um tenant e os bancos dele para LogStream e NotificationHub;
  2. cria o perfil com as permissões e um client de produto;
  3. grava uma entrada de auditoria no LogStream (`201`) e despacha uma notificação no NotificationHub (sucesso);
  4. confere que, sem a permissão, o NotificationHub responde `403`.
- Testes: o NotificationHub escolhe as implementações do SecureGate com a seção presente e as de configuração sem ela.
- Guia de implantação, CHANGELOG e README do NotificationHub atualizados. O NotificationHub não aceita mais tenant nem perfil por configuração quando está ligado ao SecureGate.

**Intranet (guia de deploy)**
- Quarto client na tabela (o de integração): como criar, o perfil e as permissões, e as variáveis.
- O client `intranet` sem `logstream`/`notificationhub`.
- Checklist e CHANGELOG.

## Riscos (ADR-0020)

- **Menos privilégio que antes:**
  - o secret de integração só alcança o tenant da Intranet (`X-Tenant-Id` de outro tenant é `400`);
  - os escopos de produto não incluem infraestrutura;
  - o client de plataforma perde dois escopos.
- **Caixa in-app:** a Intranet lê a caixa de qualquer pessoa do tenant pelo `userId` que ela informa. É o modelo "chamador confiável" já registrado na pendência do vínculo `userId` ↔ `sub`. Não muda aqui.
- **NotificationHub ligado ao SecureGate:** com o SecureGate fora do ar, ele vale do cache e depois falha fechado, como o LogStream (ADR-0021).
