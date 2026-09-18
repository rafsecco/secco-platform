# Credenciais por e-mail — design

**Data:** 2026-09-17
**Status:** aguardando revisão
**Decisão arquitetural:** [ADR-0033](../../adr/secco-platform-adrs.md) — capacidade de e-mail da plataforma e ciclo de credencial
**Origem:** entrega B da decomposição da gestão de identidade. Depende da entrega A (ADR-0032): todo evento de senha
revoga sessões pela operação única já implementada. A entrega C (conta: trocar e-mail, desvincular Entra, telas)
usa a mesma porta de e-mail.

## Contexto

Hoje o admin digita a senha inicial do usuário (`CreateUser`, campo `password`), não existe "esqueci minha senha" e
o usuário não troca a própria senha. A consequência é que **a credencial nunca pertence só ao dono** e perder a
senha significa pedir a um admin que escolha outra.

Dois achados do levantamento condicionam o desenho:

1. O `Secco.NotificationHub` **já tem** `IEmailSender` com adaptadores MailKit e SendGrid na própria
   Infrastructure (`src/NotificationHub/Secco.NotificationHub.Infrastructure/Email/`); MailKit e SendGrid já estão
   no `Directory.Packages.props`.
2. **Não há `AddDataProtection` em lugar nenhum do monorepo.** Com o padrão, as chaves ficam na máquina e somem a
   cada reinício — os links de convite/redefinição morreriam sozinhos, e os cookies de login já sofrem disso.

## Decisões (aprovadas na conversa de design)

1. **`Secco.SDK.Email`**: pacote fino novo com a porta e os adaptadores extraídos do NotificationHub, adotado pelos
   dois produtos. O SecureGate **não** depende do NotificationHub.
2. **Tokens do Identity** (convite 72 h, redefinição 30 min) com **chaves de Data Protection no banco** do SecureGate.
3. **O admin nunca define senha**: `password` sai do `CreateUser` (quebra assumida) e entra o convite.
4. **Limite por conta e por IP** no "esqueci minha senha", com resposta sempre idêntica.
5. **Conta só corporativa** (`fl_local_login_enabled` desligado): sem senha, sem convite, sem recuperação.
6. **Quatro eventos auditados** (senha, convite, pedido de recuperação, falha de link), **best-effort**.

## Secco.SDK.Email (pacote novo)

- `ISeccoEmailSender.SendAsync(string recipient, string subject, string body, CancellationToken)`.
- `SeccoSmtpEmailSender` (MailKit) e `SeccoSendGridEmailSender`, mais `SeccoEmailOptions`
  (`Provider`, `ApiKey`, `Host`, `Port`, `UseStartTls`, `Username`, `Password`, `FromAddress`, `FromName`) e
  validação equivalente à atual — promoção sem mudança de comportamento.
- `AddSeccoEmail(this IServiceCollection, SeccoEmailOptions)`: registra o adaptador conforme o provider.
- Cada produto lê **sua** seção: `NotificationHub:Email` (existente) e `SecureGate:Email` (nova).
- O NotificationHub adota e apaga as cópias; seus testes de SMTP/SendGrid migram para o pacote.

## SecureGate — configuração

| Chave | Regra |
| --- | --- |
| `SecureGate:Email` | Obrigatória fora de Development (fail-fast). Em DEV aponta para o MailHog do compose. |
| `SecureGate:PublicBaseUrl` | Base dos links. Obrigatória fora de Development. **Nunca** derivar do header `Host`. |
| `SecureGate:Credentials:InviteLifetimeHours` | Padrão 72, teto no código. |
| `SecureGate:Credentials:ResetLifetimeMinutes` | Padrão 30, teto no código. |
| `SecureGate:Credentials:ForgotPerAccountPerHour` | Padrão 3. |
| `SecureGate:Credentials:ForgotPerIpPerHour` | Padrão 10. |
| `SecureGate:Audit` | Identidade de auditoria; aceita o nome antigo `SecureGate:ElevationAudit` com aviso. |

Data Protection: `AddDataProtection().PersistKeysToDbContext<SecureGateDbContext>()`
(`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`), tabela de chaves pela convention da ADR-0017.

Provedores de token: dois `DataProtectorTokenProvider` nomeados — `SeccoInvite` e `SeccoReset` — com os
`TokenLifespan` acima.

## Fluxos

| Fluxo | Página / rota | Efeitos |
| --- | --- | --- |
| Convite | e-mail → `/conta/definir-senha` | Define senha; stamp muda (invalida links pendentes); auditoria; sem e-mail de aviso (não havia senha antes) |
| Esqueci minha senha | `/conta/esqueci` (pública) | Resposta idêntica sempre; envia link só se conta existe, ativa, tenant ativo e login local habilitado; auditoria do pedido |
| Redefinir | e-mail → `/conta/redefinir-senha` | Valida token/expiração/estado; define senha; **revoga todas as sessões**; e-mail de aviso; auditoria |
| Trocar a própria senha | `/conta/trocar-senha` (autenticada) | Exige a senha atual; revoga sessões e **renova o cookie** — outras sessões caem, a atual segue; e-mail de aviso; auditoria |
| Redefinição pelo admin | `POST .../users/{userId}/password-reset` | Envia link **e revoga sessões na hora**; auditoria |

Link expirado ou já usado leva a uma tela com "pedir outro", nunca a erro seco. O mesmo fluxo de recuperação
atende convite expirado.

## API

**Quebra:** `CreateUser` perde `password` e ganha `localLogin` (bool, padrão `true`): `true` cria sem senha e envia
convite; `false` cria conta só corporativa (`fl_local_login_enabled` desligado), sem senha e sem convite.

| Operação | Rota | Regras |
| --- | --- | --- |
| `ResendUserInvite` | `POST .../users/{userId}/invite` | 204; 409 se já tem senha ou login local desabilitado; 404 cross-tenant |
| `ResetUserPassword` | `POST .../users/{userId}/password-reset` | 204 + revogação; 409 se login local desabilitado; 404 cross-tenant |
| `SetUserLocalLogin` | `POST .../users/{userId}/local-login` (`{ "enabled": bool }`) | Habilitar envia convite; desabilitar **apaga a senha e revoga as sessões**; 404 cross-tenant |

`UserDetailDto` ganha `hasPassword` e `localLoginEnabled` — é o que a tela usa para escolher entre **Reenviar
convite**, **Redefinir senha** ou nenhum dos dois.

Schema: coluna `fl_local_login_enabled` em `tb_users` (padrão ligado), migrations nos dois engines.

## AdminPortal (mínimo nesta entrega)

- Criar usuário: sai o campo de senha, entra a escolha "com senha local" / "só login corporativo".
- Página do usuário: **Reenviar convite** ou **Redefinir senha**, conforme `hasPassword` e `localLoginEnabled`.
- Trocar e-mail, desvincular Entra e encerrar sessões seguem na entrega C.

## Segurança (ADR-0020)

- Link montado de `SecureGate:PublicBaseUrl`; header `Host` nunca entra.
- Resposta do "esqueci" idêntica em todos os casos; o envio acontece fora do caminho da resposta, para o tempo não
  denunciar existência de conta.
- Limite por conta e por IP com o limitador nativo do ASP.NET (sem pacote novo); exceder devolve a mesma resposta.
- Nunca logar token nem senha; auditoria leva usuário, tenant e ação.
- Páginas com antiforgery, como o login.
- Conta desativada, bloqueada, de tenant inativo ou só corporativa: mesma resposta genérica, sem link.
- **Fora de escopo, registrado:** cifrar as chaves de Data Protection em repouso.

## Testes

- **Ponta a ponta pelas telas:** convite → definir senha → login; esqueci → redefinir → sessão antiga recusada;
  troca da própria senha mantendo a sessão atual e derrubando outra.
- **Negativos:** link expirado; link já usado (stamp mudou); conta desativada, tenant inativo, conta só
  corporativa; token de outra conta; senha fraca; `ResendUserInvite` em conta com senha (409).
- **Enumeração:** e-mail existente e inexistente devolvem exatamente a mesma resposta; idem ao exceder o limite.
- **Auditoria best-effort:** LogStream fora do ar não impede redefinir nem revogar.
- **E-mail:** remetente falso prova destinatário e link corretos e **nenhuma senha no corpo**.
- **`Secco.SDK.Email`:** testes migrados do NotificationHub seguem verdes nos dois lados.
- **Mutação:** base pública do link, resposta genérica, limite, uso único, recusa de conta corporativa, revogação
  nos eventos de senha.

## Publicação

`Secco.SDK.Email` **0.1.0** (pacote novo — entra na lista de tags, no mapeamento tag→projeto do workflow e na
tabela da skill de release) e `Secco.SecureGate.Client` **0.9.0** (quebra do `CreateUser`), com a cadeia do MinVer
conferida por `scripts/check-release-chain.py`. CHANGELOG destaca a quebra e a nova exigência de configuração de
e-mail.

## Fora desta entrega

- Trocar e-mail, desvincular Entra e as telas correspondentes (entrega C).
- Segundo fator no login local (entrega D).
- Cifrar as chaves de Data Protection em repouso.
- Envio pelo NotificationHub (descartado na ADR-0033).
