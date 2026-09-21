# Conta do usuário: troca de e-mail e desvínculo do Entra — design

**Data:** 2026-09-21
**Status:** aguardando revisão
**Decisões arquiteturais:** [ADR-0033](../../adr/secco-platform-adrs.md) (ciclo de credencial), [ADR-0026](../../adr/secco-platform-adrs.md) (federação Entra), [ADR-0032](../../adr/secco-platform-adrs.md) (revogação de sessão), [ADR-0034](../../adr/secco-platform-adrs.md) (idempotência), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança)
**Origem:** entrega C da decomposição da gestão de identidade. Depende da B: reusa a porta de e-mail, os provedores de token, a trilha best-effort e as telas de conta. A entrega D (segundo fator) vem depois.

## Contexto

A entrega B tirou a senha das mãos do admin, mas deixou dois dados da conta sem dono:

1. **O e-mail não se troca.** Ele é o *username*, é único global e não há tela nem endpoint para alterá-lo — quem casa, muda de endereço ou teve erro de digitação na criação fica preso ao valor original.
2. **O vínculo com o Entra ID não se desfaz.** A ADR-0026 grava `{tid}:{oid}` no primeiro login federado e nunca mais o solta. Conta recriada no diretório (novo `oid`) ou pessoa que saiu de lá ficam com um vínculo morto que ninguém remove.

Dois fatos do mecanismo atual condicionam o desenho, e foram confirmados no código
(`EntraSignInProcessor`):

- **O e-mail só governa o login federado no PRIMEIRO acesso.** Depois disso vale o vínculo `oid`. Trocar o e-mail é, portanto, perigoso antes do primeiro login federado e inócuo depois.
- **Desvincular não bloqueia ninguém.** Enquanto a pessoa seguir no diretório e a federação do tenant estiver ligada, o login seguinte casa por e-mail e vincula de novo.

## Decisões (aprovadas na conversa de design)

1. **Só o dono troca o próprio e-mail**, com confirmação no endereço novo. O admin fica de fora: como o e-mail é o username, um admin trocando e-mail redirecionaria a conta de outra pessoa para uma caixa que ele controla.
2. **Pedir a troca exige a senha atual** de quem tem senha local. É o que quebra a cadeia "cookie roubado → troca o e-mail → esqueci minha senha na caixa nova". Conta só corporativa não tem senha a exigir e troca apenas com o link — nela o e-mail já não governa o login.
3. **Desvincular é operação do admin** (`securegate:admin`), recusada quando deixaria a conta sem caminho de entrada.
4. **Nenhuma migration**: o endereço pendente viaja dentro do token, não numa coluna.
5. **A troca de e-mail não tem endpoint de API** — vive só nas telas, pela mesma razão da ADR-0033.

## Troca de e-mail

### Fluxo

| Passo | Onde | O que acontece |
| --- | --- | --- |
| Pedir | `/conta/trocar-email` (autenticada) | Novo e-mail + senha atual (o campo de senha não existe para conta só corporativa). Valida a senha, gera o token e envia |
| Avisar | e-mail antigo | Aviso **sem link**: "pediram a troca do e-mail desta conta para `n***@exemplo.com`" |
| Confirmar | e-mail novo → `/conta/confirmar-email` | Token conferido **na abertura da página**; efetiva a troca |
| Depois | — | `Email` e `UserName` trocados juntos; sessões revogadas e a desta janela renovada |

### Regras

- **O token embute o endereço novo** (convenção `ChangeEmail:{novo}` do próprio Identity, via `GenerateChangeEmailTokenAsync`). Um link interceptado só serve para aquele destino — não redireciona a conta para outro lugar.
- **Validade de 30 minutos**, pelo provedor nomeado `SeccoEmailChange` ligado em `IdentityOptions.Tokens.ChangeEmailTokenProvider`, com a janela lida de `SecureGate:Credentials:ResetLifetimeMinutes`.
- **E-mail já em uso responde igual a e-mail livre:** mesma mensagem, nada enviado. A alternativa contaria a um usuário autenticado quais endereços existem na instalação (ADR-0020). O custo assumido é que quem digita errado descobre pela ausência do e-mail, não pela tela.
- **`Email` e `UserName` mudam na mesma operação**, porque na plataforma são a mesma coisa (ADR-0022). A troca move o `SecurityStamp`, então **as sessões caem** (ADR-0032) e os links de convite e redefinição pendentes morrem junto — de graça.
- **A sessão de quem trocou é renovada** em seguida, como na troca de senha: ninguém é deslogado pelo próprio ato.
- **O `sub` dos tokens não muda** (é o `Guid` do usuário), então nenhum produto quebra. A trilha de auditoria guarda o e-mail como *snapshot*: entradas antigas seguem com o endereço antigo, que é o comportamento correto para auditoria.

## Desvínculo do Entra ID

`DELETE /api/v1/tenants/{tenantId}/users/{userId}/external-logins/{provider}`, escopo `securegate:admin`.

| Situação | Resposta |
| --- | --- |
| Vínculo removido | `204` |
| Não havia vínculo | `204` — idempotente de fato (ADR-0034), sem efeito colateral repetido |
| Conta com **login local desligado** | `409` — o vínculo é o único caminho de entrada |
| Usuário de outro tenant, ou inexistente | `404`, a mesma resposta nos dois casos (ADR-0020) |

**"Sem senha definida ainda" não impede.** Com login local ligado, a pessoa chega à senha pelo convite ou pelo "esqueci minha senha" (ADR-0033). Para desvincular quem só entra pelo diretório, o admin liga a senha local primeiro — o que dispara convite — e depois desvincula.

**Limite honesto, que vai na tela e no README:** desvincular não bloqueia ninguém. Quem quer barrar acesso **desativa a conta** ou desliga a federação do tenant.

## Telas

- **SecureGate `/conta`:** entra "Trocar meu e-mail", ao lado de "Trocar minha senha".
- **AdminPortal, página do usuário:** a lista de logins externos ganha **Desvincular** com confirmação. Em conta só corporativa o botão aparece **desabilitado com a explicação**, e não some sem dizer nada.
- **O AdminPortal não ganha troca de e-mail** — decorre de só o dono poder trocar.

## Auditoria

Três eventos novos na trilha best-effort da ADR-0033: `credencial.email-troca-solicitada`, `credencial.email-trocado` e `credencial.vinculo-externo-removido`. Nenhum deles leva token; o evento de troca registra que houve troca, não o endereço anterior em claro no metadata.

## Segurança (ADR-0020)

- Senha atual exigida no pedido, quebrando a cadeia de sequestro por cookie.
- Link só para o endereço novo; aviso ao antigo **sem link**, para não criar um segundo alvo de phishing.
- Resposta idêntica para e-mail livre e e-mail em uso.
- Token com validade curta, uso único pelo `SecurityStamp`, e conferido na abertura da página.
- Guarda de conta órfã no desvínculo, e nenhuma promessa falsa de bloqueio.
- Limite de taxa do pedido de troca reusa o limitador da ADR-0033, por conta e por IP.

## Testes

- **Ponta a ponta:** pedir → link no endereço novo → confirmar → login com o novo funciona e com o antigo não.
- **Negativos:** senha atual errada; link expirado, já usado ou emitido para outro endereço; e-mail já em uso (resposta genérica, nada enviado); conta só corporativa pedindo troca sem senha.
- **Sessão:** a troca derruba as outras sessões e mantém a desta janela; convite pendente morre junto.
- **Desvínculo:** `409` em conta só corporativa, `204` idempotente, `404` cross-tenant, e vínculo de fato removido no banco.
- **Mutação**, oito invariantes: link para o endereço antigo, troca sem exigir senha, token de outro endereço aceito, e-mail duplicado aceito, desvincular sem guarda, ausência de revogação, `UserName` não acompanhando o `Email`, e resposta distinguível para e-mail em uso.

## Publicação

`Secco.SecureGate.Client` **0.10.0** (aditivo: uma rota nova) e os patches de cadeia do MinVer. Sem pacote novo e sem quebra.

## Fora desta entrega

- Segundo fator no login local (entrega D).
- Admin trocar o e-mail de alguém.
- O próprio usuário desvincular a conta do diretório — enquanto ele seguir no diretório, o login seguinte vincularia de novo.
- Cifrar as chaves de Data Protection em repouso (registrado na ADR-0033).
- Retenção e LGPD.
