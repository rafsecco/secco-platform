# Segundo fator no login local — design

**Data:** 2026-09-23
**Status:** aguardando revisão
**Decisões arquiteturais:** [ADR-0022](../../adr/secco-platform-adrs.md) (login de usuário), [ADR-0033](../../adr/secco-platform-adrs.md) (ciclo de credencial), [ADR-0032](../../adr/secco-platform-adrs.md) (revogação de sessão), [ADR-0026](../../adr/secco-platform-adrs.md) (federação), [ADR-0030](../../adr/secco-platform-adrs.md) (operador de instalação), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança)
**Origem:** entrega D da decomposição da gestão de identidade, a última da série. Depende de B (porta de e-mail, trilha, telas de conta) e de C (a página `/conta` onde o cadastro vive).

## Contexto

O login local é o de senha da ADR-0022, e senha sozinha é o que vaza em todo incidente de terceiro: reuso entre sistemas, phishing e credential stuffing não são resolvidos por política de complexidade. As entregas A a C fecharam o ciclo em volta da senha — revogação efetiva, credencial que pertence ao dono, e-mail sob controle do usuário —, mas o fator continua único.

O peso disso não é igual em todas as contas. O papel `installation-operator` (ADR-0030) lê log de **todos** os tenants por elevação (ADR-0031) e gere identidade da instalação inteira. É a conta que compensa atacar.

Dois fatos do que já existe encurtam a entrega, confirmados no código:

- **`fl_two_factor_enabled` já está em `tb_users`** desde a migration inicial — é propriedade padrão do `IdentityUser`.
- **`tb_user_tokens` já existe** e é onde o ASP.NET Identity guarda a chave do autenticador e os códigos de recuperação.

Portanto: **nenhuma migration**.

## Decisões (aprovadas na conversa de design)

1. **TOTP, e só.** Sem SMS e sem e-mail como segundo fator: SMS cai para SIM swap e e-mail não é segundo fator quando o próprio e-mail já recupera a senha.
2. **Voluntário para todos, obrigatório para o operador de instalação.** O admin do tenant não força 2FA nos usuários dele nesta entrega.
3. **Códigos de recuperação (10, uma vez, só hash) mais reset pelo admin.** Reset **não isenta**: zera o cadastro.
4. **Sem "lembrar deste dispositivo".** O cookie de dispositivo confiável é o que um invasor com acesso à máquina rouba para pular o segundo fator, e contornaria a revogação de sessão da ADR-0032.
5. **Login federado não pede dígito** — o MFA é do diretório (ADR-0026).

## Cadastro

Página `/conta/dois-fatores`, autenticada pelo cookie do login interativo.

| Estado | O que a tela faz |
| --- | --- |
| Sem 2FA | Mostra a chave em blocos legíveis e um **QR code gerado no servidor**, embutido como `data:` URI |
| Confirmando | Exige **um código válido** antes de ligar — sem isso, um autenticador mal configurado trancaria a conta no login seguinte |
| Recém-ligado | Mostra os **10 códigos de recuperação uma única vez**, com aviso explícito de que não voltam |
| Ligado | Oferece **gerar novos códigos** e **desligar** (exceto operador, ver abaixo) |

O QR code sai do servidor de propósito: qualquer gerador externo receberia o segredo TOTP do usuário, o que anula o segundo fator antes mesmo de ele existir (ADR-0020).

## Login

1. Senha, como hoje.
2. Se a conta tem 2FA, a segunda tela (`/login/dois-fatores`) pede **o dígito ou um código de recuperação**.
3. Entre as duas, o estado vive no **cookie de duas etapas do próprio Identity** (`IdentityConstants.TwoFactorUserIdScheme`), que não autentica nada sozinho.

Regras:

- **Cada código de recuperação vale uma vez.** O Identity remove o usado; a tela avisa quantos restam.
- **Tentativa errada alimenta o lockout** que a ADR-0020 já configurou — força bruta no dígito é o ataque óbvio contra TOTP.
- **Ligar ou desligar o 2FA encerra as outras sessões** (ADR-0032) e avisa o dono por e-mail, como todo evento de credencial.
- **Login federado não passa por aqui.**

## Obrigatoriedade para o operador de instalação

- Quem tem `installation-operator` e **ainda não cadastrou** não completa o login: é levado ao cadastro e só sai de lá com o segundo fator ligado. Como o bloqueio acontece **antes de o código de autorização ser emitido**, o AdminPortal não precisa saber de nada — ele é relying party (ADR-0023).
- **O operador não desliga o próprio 2FA.** Mesma razão de ninguém desativar a própria conta nem se remover do papel de operador (issue #26): senão a exigência vira decoração.
- Um operador pode **resetar** o 2FA de outro (ver abaixo); o que ele não pode é ficar sem.

## Reset pelo admin

`POST /api/v1/tenants/{tenantId}/users/{userId}/two-factor/reset`, escopo `securegate:admin`.

- Apaga a chave do autenticador e os códigos de recuperação; a conta volta a "sem 2FA cadastrado".
- **Não isenta ninguém.** Sendo conta de operador, o próximo login cai direto no cadastro.
- **Audita e avisa o dono por e-mail.** É exatamente a operação que um admin comprometido usaria para contornar o segundo fator de outra pessoa: precisa deixar rastro e chegar à caixa da vítima.
- `404` para usuário de outro tenant, mesma resposta de inexistente (ADR-0020). Idempotente: conta sem 2FA responde `204` igual.

## Auditoria

Quatro eventos novos na trilha best-effort da ADR-0033: `credencial.2fa-ligado`, `credencial.2fa-desligado`, `credencial.2fa-resetado` e `credencial.2fa-codigo-recuperacao-usado`. Nenhum leva chave nem código.

## Segurança (ADR-0020)

- Segredo TOTP nunca sai do servidor: QR gerado localmente, nada de gerador externo.
- Códigos de recuperação só como hash, exibidos uma vez.
- Tentativa de dígito alimenta o lockout; o cookie de duas etapas não autentica sozinho.
- Reset pelo admin audita e notifica — é o caminho de contorno, e por isso é o mais vigiado.
- Nenhum segredo de longa duração no navegador, porque não há "lembrar dispositivo".
- Operador não se desliga: a política não depende da boa vontade de quem ela protege.

## Testes

- **Ponta a ponta:** cadastrar com confirmação; login em dois passos; dígito errado recusado; código de recuperação funciona **uma vez** e não na segunda.
- **Operador:** sem 2FA é levado ao cadastro e não obtém código de autorização; não consegue desligar o próprio.
- **Reset:** zera o cadastro, audita, avisa, `204` idempotente, `404` cross-tenant.
- **Federado:** login pelo Entra não pede dígito nem é bloqueado pela exigência do operador.
- **Sessão:** ligar e desligar derrubam as outras sessões.
- **Mutação**, oito invariantes: confirmação do cadastro, uso único do código, lockout no dígito, bloqueio do operador, operador desligando o próprio, reset isentando em vez de zerar, revogação nos dois eventos e o `404` cross-tenant do reset.

## Publicação

`Secco.SecureGate.Client` **0.11.0** (aditivo: uma rota) e os patches de cadeia do MinVer.

## Fora desta entrega

- "Lembrar deste dispositivo".
- 2FA obrigatório por tenant, decidido pelo admin.
- SMS e e-mail como segundo fator.
- Exigir 2FA (ou reautenticação) na elevação da ADR-0031 — candidato real, registrado.
- WebAuthn/passkeys.
