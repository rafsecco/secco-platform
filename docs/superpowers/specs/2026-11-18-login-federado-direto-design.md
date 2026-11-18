# Login federado direto — sem a tela do SecureGate quando o Entra já autentica

**Data:** 2026-11-18
**Origem:** pedido do `secco-intranet` (ABBC), sobre o login federado da ADR-0026 e as contas provisionadas pelo diretório da ADR-0055
**Decisão arquitetural:** sem ADR nova (chamada minha, a confirmar). A mudança fica só no SecureGate, é opt-in por tenant e se reverte desligando a opção. Não muda contrato entre produtos além de um campo na federação. A recusa do `prompt=none` vai para o `design-decisions-log.md`.

## Por quê

Na ABBC, as contas nascem pelo grupo do Entra, sem senha local, e as máquinas estão ingressadas no Entra (PRT) ou usam Seamless SSO. Quem abre a intranet sem sessão cai na tela de login do SecureGate e precisa clicar em "Entrar com Microsoft". O Entra então autentica sem pedir nada. Esse clique é o único passo manual que sobra, e ele não protege nada: para essas pessoas, a tela local não tem outra opção útil.

## Objetivos

- Em tenant com a opção ligada, abrir um produto sem sessão leva direto ao Entra e volta logado, sem a tela do SecureGate.
- Com a opção desligada, ou em tenant sem federação, nada muda.
- A tela local continua alcançável, como saída de emergência para o operador e para contas com senha local.
- Nenhum caminho forma laço de redirecionamento: nem erro do Entra, nem consentimento retirado, nem conta recusada, nem logout.
- O logout não leva a um login automático imediato: a pessoa vê "Você saiu" e escolhe entrar de novo.
- O guia documenta o que precisa existir do lado do cliente para o Entra autenticar sem perguntar.

## Não objetivos

- **Logout no Entra.** A saída encerra só a sessão do SecureGate. Sair do Entra derrubaria o Office, o Teams e o Windows da pessoa. A federação é só autenticação (ADR-0026).
- **`prompt=none` ao Entra com repetição em caso de falha.** Foi avaliado e descartado (ver "Abordagens").
- **Modo direto para client sem tenant** (AdminPortal e demais clients de plataforma). Sem tenant não há federação a consultar. O operador segue na tela local com segundo fator.
- **Lembrar o e-mail da última pessoa** para mandar como `login_hint`. Isso seria um cookie com dado pessoal. O `login_hint` vem só da aplicação.
- **Mudança no `secco-intranet`** ou em qualquer relying party. O `post_logout_redirect_uri` continua o mesmo.

## Anti-objetivos (fracassos mesmo que "funcione")

- **Laço silencioso.** Uma conta que o Entra autentica e o SecureGate recusa não pode ficar indo e voltando entre os dois.
- **O operador ou a conta só local ficam sem caminho de entrada** num tenant com a opção ligada.
- **A decisão de ir direto depende de dado da requisição**, como parâmetro ou URL reinterpretada, em vez do registro do client (ADR-0040).
- **O logout vira um "entrar de novo".** O PRT autentica em silêncio, então sair e ser logado de volta sem ação nenhuma é exatamente o que não pode acontecer.
- **A recusa revela o motivo.** A mensagem segue a genérica de hoje (ADR-0020).

## Abordagens consideradas

1. **Decisão no `/connect/authorize`**: escolhida pelo usuário. Sem sessão, o authorize já sabe o tenant pelo registro do client. Ele lê a federação e desafia o esquema do Entra. A tela `/login` nunca redireciona sozinha, então a saída de emergência existe sem parâmetro novo e todo erro termina numa página parada.
2. **Decisão na tela `/login`**: descartada. O `OnGet` teria de reinterpretar o `returnUrl` para achar o `client_id`. Isso é entrada externa re-analisada fora do OpenIddict, que já validou o pedido. A saída de emergência passaria a exigir `?local=1`, e cada caminho de erro precisaria lembrar de passá-lo.
3. **Esquema OIDC por tenant**: descartada. A ADR-0026 fixou um esquema único e estático.

Sobre o **`prompt=none`**: com o endpoint do diretório fixado, mais `domain_hint` e `login_hint`, o Entra já autentica sem interação quando há PRT ou Seamless SSO. Quando o `prompt=none` falha, a repetição sem ele mostra a mesma tela que o Entra mostraria de início. O ganho é nulo, e o custo é uma ida e volta a mais e um estado a mais para evitar laço. Descartado.

Sobre o **logout**, o usuário escolheu a página "Você saiu" **no SecureGate**, e não uma página pública em cada aplicação. Assim funciona para qualquer relying party sem mudança nele.

## Desenho

### Configuração (por tenant, só o operador)

- `TenantFederation` ganha **`DirectSignIn`** (coluna `fl_direct_sign_in`, `bool`, padrão `false`) e o método `SetDirectSignIn(bool)`. O valor só tem efeito com `IsEnabled = true`.
- `PUT /api/v1/tenants/{id}/federation`: o `UpsertTenantFederationRequest` ganha `DirectSignIn` (`bool?`, **no fim** do record). Ausente, mantém o valor atual, como `AllowedEmailDomains`. A rota segue só do `securegate:admin`. O admin de tenant não tem esta rota (ADR-0026/0052).
- `TenantFederationDto` ganha `DirectSignIn` **no fim** (o record é posicional e consumido pelo NSwag).
- **Migration nos dois engines.** `tb_tenant_federations` está sob RLS, então a migration chama `TenantRowLevelSecurity.Drop` no começo e `Create(CurrentRevision)` no fim. É só uma coluna nova, sem revisão nova do helper.
- `openapi.json` e `Secco.SecureGate.Client` regenerados no mesmo commit. O pacote vai a **0.26.0**, aditivo e sem quebra.
- **AdminPortal:** na seção Federação da página do tenant, uma caixa "Ir direto ao Entra (sem a tela de login do SecureGate)", com a situação exibida na leitura.

### Quando o authorize vai direto ao Entra

Tudo abaixo precisa valer ao mesmo tempo. Se um item falhar, o comportamento é o de hoje: desafio do Identity e tela `/login`.

1. A requisição **não tem cookie de sessão nenhum** (`AuthenticateAsync` falhou). Os dois caminhos que já têm cookie e o derrubam continuam indo para a tela local, sem mudança: stamp inválido ou conta recusada pelo `AccountStateGuard`. **Este é o freio principal contra laço.** O processador do Entra não confere `CanSignInAsync`, e o `AccountStateGuard` confere. Uma conta aceita por um e recusada pelo outro iria ao Entra e voltaria, sem fim.
2. O client da requisição é **vinculado a tenant** (`OidcApplication.TenantId`, ADR-0040). É o mesmo dado que o `LoginTenantResolver.ForAuthorizeAsync` usa.
3. O tenant tem federação com `IsEnabled` e `DirectSignIn`, e o esquema do Entra está configurado nesta instalação (`SecureGateEntraIdOptions.IsConfigured`).
4. **Não há marca de logout** (ver "Logout").
5. **Não há marca de tentativa direta recente.** É a segunda defesa contra laço: um cookie curto gravado ao desafiar o Entra e apagado no callback bem-sucedido. Se ele existir quando o authorize chegar de novo sem sessão, alguma volta falhou sem chegar ao callback, e o authorize vai para a tela local com a mensagem genérica.

Com `prompt=none` vindo da aplicação, sem sessão e no modo direto, a resposta é **`login_required`** ao `redirect_uri`, sem desafio nenhum (OIDC Core §3.1.2.6). Fora do modo direto, segue o comportamento de hoje.

A federação é lida no escopo do tenant do login (`ITenantAccessScope.EnterTenant`), dentro do escopo de plataforma que o authorize já declara. A lista `platform-scope-allowlist.txt` não ganha entrada nova.

### O desafio ao Entra

- `Challenge` do esquema `SecureGateEntraIdOptions.AuthenticationScheme` com `RedirectUri = /login/entra-callback?returnUrl=<a requisição de authorize>`. O callback e o `EntraSignInProcessor` **não mudam**: pin do `tid`, casamento por `oid`/e-mail, provisionamento e reconciliação de perfis continuam iguais.
- Os dados por requisição viajam em `AuthenticationProperties.Items` (protegidos pelo state do handler): o directory id, o `domain_hint`, o `login_hint` e se há reautenticação forçada.
- Um novo `OnRedirectToIdentityProvider` no esquema, que só age quando esses itens existem:
  - troca o segmento de tenant do endpoint de autorização (`organizations`) pelo `DirectoryId` em formato `D`. O endereço é derivado do `AuthorizationEndpoint` da configuração, sem montar o host à mão, para manter nuvens soberanas. Se o segmento não for encontrado, o endpoint fica como está e o `domain_hint` segue sozinho;
  - `domain_hint` = o **primeiro** domínio de `AllowedEmailDomains`, quando houver. O dado já está normalizado no banco;
  - `login_hint` = o `login_hint` da requisição de authorize, **só** se tiver até 254 caracteres, nenhum caractere de controle e formato de e-mail. Caso contrário, é descartado em silêncio (ADR-0020);
  - `prompt=login` quando a aplicação pediu reautenticação.
- **Troca do code:** continua no endpoint de token do `organizations`. **Confirmar contra um tenant Entra real** que o code emitido pelo endpoint do diretório é aceito ali. Se não for, ajustar o endpoint de token no `OnAuthorizationCodeReceived`. Esse ponto é verificação manual de aceite, porque o CI não alcança o Entra.

### Reautenticação (ADR-0049/0050)

O caminho `prompt=login` / `max_age` vencido hoje derruba o cookie e desafia o Identity. Isso não cai na regra 1, porque havia cookie. No modo direto (itens 2 e 3), o desafio vai ao Entra **com `prompt=login`** e volta pelo mesmo `WithoutLoginPrompt`. O callback já chama `FreshAuthentication.Mark`, então a idade da autenticação se renova. Fora do modo direto, nada muda.

### Erro e consentimento retirado

- `OnRemoteFailure` (erro devolvido pelo Entra, consentimento retirado, pessoa cancelou) e a recusa do callback seguem indo para `/login?federatedError=1`, com a mensagem genérica de hoje. **Mudança:** o `returnUrl` passa a ser preservado, só se for local. Ele é lido do `RedirectUri` das propriedades no `OnRemoteFailure` e do parâmetro no callback. Assim o login local, ou um novo clique em "Entrar com Microsoft", retoma a requisição de authorize em vez de cair em `/conta`.
- A tela `/login` **nunca redireciona sozinha.** Todo caminho de erro termina numa página parada, e uma nova tentativa exige um clique.
- A conta autenticada que o authorize recusa por tenant (`CanIssueAsync` falso) recebe `access_denied` ao `redirect_uri`, como hoje. A sessão existe, então nada volta ao Entra.

### Logout

- O `/connect/logout` passa a gravar a **marca de logout**: cookie `HttpOnly`, `SameSite=Lax`, `Secure` pela mesma política do cookie de sessão, de sessão do navegador e com teto de 12 h. O valor não importa, só a presença, então adulterar a marca só faz a página aparecer. Ela é gravada sempre, mas só é lida no modo direto, então com a opção desligada nada muda de comportamento.
- A aplicação recebe a pessoa no `post_logout_redirect_uri`, desafia, e o authorize, sem sessão e com a marca, redireciona para **`/saiu?returnUrl=<authorize>`**.
- **`/saiu`** (Razor, anônima, mesmo layout e CSP com nonce da `/login`):
  - título "Você saiu" e o texto "Sua sessão foi encerrada.";
  - botão **Entrar**: `POST` com antiforgery que apaga a marca e redireciona ao `returnUrl` local. O authorize então vai direto ao Entra;
  - link "Entrar com e-mail e senha", que leva a `/login?returnUrl=...`;
  - aberta sem `returnUrl`, o botão leva a `/conta`.
- Qualquer login bem-sucedido, local ou federado, apaga a marca.

```
┌──────────────────────────────────┐
│  Você saiu                       │
│  Sua sessão foi encerrada.       │
│                                  │
│  [ Entrar ]                      │
│  Entrar com e-mail e senha       │
└──────────────────────────────────┘
```

### Saída de emergência

**`/login` direto** (sem `returnUrl`, ou com um): a tela local de sempre. Depois do login com senha (e segundo fator), a sessão do SecureGate existe. Quando a pessoa abre o produto, o authorize encontra a sessão e não vai ao Entra. O guia documenta a URL. A `/saiu` e a `/login` com erro também levam a ela.

## Segurança (ADR-0020)

- **Input externo:** só o `login_hint` vem da requisição. Ele é validado em tamanho, caracteres de controle e formato, e descartado se falhar. O `domain_hint` e o directory id vêm do banco, e o directory id entra na URL como GUID formatado, sem texto livre. O `returnUrl` segue só local (`LocalRedirect` / `IsLocalUrl`).
- **Isolamento de tenant:** a decisão vem do registro do client, nunca de parâmetro. O pin `tid == DirectoryId` do processador continua sendo o gate real. Ir direto ao diretório errado no máximo falha e cai na tela local.
- **Vazamento:** o directory id fica visível no redirect a quem tem o `client_id`. Ele não é segredo (ADR-0026), e o `client_id` é público. As mensagens de erro continuam genéricas.
- **Negação de serviço:** o laço é a forma de DoS relevante aqui, e é coberto pela regra 1 e pela marca de tentativa. A leitura da federação é uma consulta por authorize sem sessão, o mesmo custo da busca do client que já existe.
- **CSRF de login:** o desafio passa a sair de um `GET` (o authorize), não do `POST` com antiforgery da tela. O atacante consegue iniciar o fluxo, mas a identidade provada é a do PRT da própria vítima. Não há como injetar a conta do atacante. A `/saiu` usa antiforgery no botão.
- **Clickjacking:** a `/saiu` herda `frame-ancestors 'none'` (ADR-0057).
- **Dependências:** nenhuma nova.

## Testes

- Unidade da decisão "ir direto?", com cada um dos cinco itens falhando isoladamente.
- Integração com o esquema do Entra configurado e metadados fixos (sem rede), verificando:
  - authorize sem sessão, opção ligada: `302` para o endpoint de autorização **do diretório**, com `domain_hint` e `login_hint`;
  - opção desligada, federação desabilitada, client sem tenant e Entra não configurado: `302` para `/login`, como hoje;
  - com a marca de logout: `302` para `/saiu`, e o `POST` de Entrar apaga a marca e volta ao authorize;
  - com a marca de tentativa: `/login?federatedError=1`;
  - cookie com stamp inválido e conta bloqueada: tela local, nunca o Entra;
  - `prompt=none` sem sessão no modo direto: `login_required`;
  - `prompt=login` no modo direto: desafio ao Entra com `prompt=login`;
  - `login_hint` inválido (longo, com controle, sem formato): descartado;
  - `returnUrl` preservado no `federatedError` e recusado quando não local;
  - `PUT`/`GET` da federação com `DirectSignIn`, incluindo omitido = mantém.
- Bateria das invariantes contra laço e contra a decisão vinda da requisição, provada por mutação, como nas entregas anteriores.
- **Aceite manual** num tenant Entra real (ABBC ou de teste), com máquina ingressada: abrir a intranet leva direto e volta logado; o code é aceito na troca (ver "O desafio ao Entra"); sair mostra "Você saiu"; e `/login` funciona com a opção ligada.

## Documentação

- `src/SecureGate/README.md`: seção nova "Login federado direto", com o que liga, a saída de emergência (`/login`), a `/saiu`, e **os requisitos do lado do cliente**:
  - dispositivos **ingressados no Entra ID** ou em **ingresso híbrido** (PRT); ou **Seamless SSO** (Entra Connect) com `https://autologon.microsoftazuread-sso.com` na zona Intranet por GPO;
  - **Edge**: funciona de fábrica com o PRT;
  - **Chrome**: política `CloudAPAuthEnabled` habilitada (ou a extensão Windows Accounts nas versões antigas);
  - **Firefox**: política `WindowsSSO` (ou "Permitir login único do Windows para contas Microsoft, corporativas e de estudante");
  - **Acesso Condicional** pode exigir MFA ou dispositivo compatível. Nesse caso, o Entra pergunta, e isso é esperado;
  - fora dessas condições, o Entra mostra a tela de conta dele. O modo direto continua funcionando, só não fica silencioso.
- `CHANGELOG.md`, `docs/roadmap.md`, o resumo do `CLAUDE.md` e o `design-decisions-log.md` (recusa do `prompt=none` e do logout no Entra).

## O que fica com quem implementa

- O nome dos cookies das duas marcas e a duração da marca de tentativa (sugestão: 2 min).
- Onde fica a decisão "ir direto?": sugestão de uma classe na `Api/Identity`, ao lado do `LoginTenantResolver`, usada pelo authorize nos dois pontos (sem sessão e reautenticação).
- A forma exata de reescrever o endpoint de autorização no evento, respeitando o fallback.
- Texto final da `/saiu`, no tom das telas existentes.
