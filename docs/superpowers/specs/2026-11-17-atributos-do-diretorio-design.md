# Atributos de pessoa vindos do diretório federado — cargo, departamento, telefone e gestor

**Data:** 2026-11-17
**Origem:** pedido do `secco-intranet` (ABBC), sobre o provisionamento por grupo da ADR-0055 (#50)
**Decisão arquitetural:** ADR-0059 a escrever na entrega, emendando a ADR-0045 (campos do diretório) e a ADR-0055 (o que a sincronização lê e com qual consentimento)

## Por quê

A ABBC já mantém no Entra ID o cargo, o departamento, o telefone e o gestor de cada funcionário. O SecureGate só entrega nome e e-mail, então a intranet pede que alguém digite tudo de novo para o Diretório e o organograma. São dois cadastros do mesmo dado, e um deles desatualiza. O objetivo é **um lugar só para manter**: o Entra, para quem o tem.

## Objetivos

- A sincronização periódica da ADR-0055 grava `jobTitle`, `department`, o telefone comercial e o gestor de cada conta gerida pelo diretório, e reflete as mudanças na rodada seguinte.
- A API de diretório (`directory:read`, ADR-0045) e o `Secco.SecureGate.Client` expõem os quatro campos e a **origem** (`directory`), para o produto saber que não deve editá-los.
- Gestor fora do tenant, gestor desativado e ciclo de gestores não derrubam a sincronização: o gestor fica nulo e o log avisa.
- O guia de instalação documenta o consentimento `User.Read.All` e o que muda para quem já usa.

## Não objetivos

- **O nome continua do SecureGate, editável** pela pessoa e pelo admin (#30), com ou sem Entra. A sincronização não toca no `DisplayName` de conta existente. Decisão do usuário: o produto precisa funcionar igual sem Entra.
- **O SecureGate não oferece edição manual dos quatro campos.** Sem Entra, os campos ficam vazios e a origem nula, e o produto mantém o próprio cadastro, como faz hoje.
- **`accountEnabled` fica fora.** O `User.Read.All` permitiria ler esse atributo e fechar o risco assumido na ADR-0055 (conta desabilitada no Entra que ainda está no grupo). É decisão separada, com consequência de autoridade (desativar conta), e não é atributo de cadastro.
- **Nada muda no login.** Conta criada no primeiro login (JIT) recebe os atributos no ciclo seguinte. O login não ganha mais uma chamada ao Graph.
- **AdminPortal e API administrativa de usuários** não exibem os campos nesta entrega.
- **Nenhum outro atributo** do Entra (foto, localização, celular, etc.).
- O `secco-intranet` deixar de editar esses campos para pessoas com origem `directory` é trabalho do adotante, fora deste repositório.

## Anti-objetivos (fracassos mesmo que "funcione")

- **Atualizar o SecureGate sem dar o consentimento novo quebra o provisionamento ou a reconciliação de perfis.** Quem já usa a ADR-0055 não pode perder o desligamento automático por causa de um campo de cadastro.
- **Uma leitura ruim do Graph apaga o cargo de toda a empresa.** Exemplos: uma resposta parcial, ou propriedades que voltam nulas por falta de permissão em vez de 403.
- **O diretório de um tenant revela gestor que não pertence a ele** (ADR-0040: uma pessoa pode ser membro de vários tenants).
- **Texto do diretório do cliente chega cru ao banco, ao log ou à API** (ADR-0020).

## Abordagens consideradas

1. **Etapa própria no ciclo, com os campos em colunas da `tb_users`** — escolhida. Depois do provisionamento e dos perfis, uma leitura separada dos membros do grupo de provisionamento traz os quatro atributos. A falha dessa leitura, inclusive o 403 sem `User.Read.All`, não afeta contas nem perfis. Custo: uma leitura a mais por ciclo, que pode virar uma chamada por pessoa para o gestor (ver "Leitura do Graph").
2. **Ampliar a leitura que o provisionamento já faz** (`$select` maior na mesma chamada). Seria uma chamada só, mas sem o consentimento novo a leitura inteira volta 403 e o provisionamento para, que é o primeiro anti-objetivo. Evitar isso exige repetir a leitura no formato antigo, o que vira a opção 1 com mais complexidade. Descartada.
3. **Igual à 1, mas em tabela própria (`tb_directory_attributes`, 1:1).** Deixaria a `tb_users` só com identidade. Custa mais uma tabela sob RLS (nova revisão do helper e outro filtro de tenant) e um join em toda leitura do diretório, o que não compensa para quatro campos. Descartada.

O usuário escolheu a 1.

## Desenho

### Quais contas recebem os atributos

As **contas geridas pelo diretório** (`DirectoryManaged = true`, ADR-0055), donas do tenant, vinculadas por `{tid}:{oid}` e presentes no **grupo de provisionamento** do tenant. Em resumo, é a mesma população que o provisionamento já administra.

- **Tenant sem grupo de provisionamento marcado:** a etapa não roda. Os atributos já gravados ficam como estão, sem atualização (congelados), e a origem continua `directory`. Isso é coerente com desmarcar o grupo, que desliga o que o diretório decide (ADR-0055).
- **Conta gerida que saiu do grupo:** já vai ser desativada pelo provisionamento. Os atributos ficam como estavam.
- **Conta só local, ou federada não gerida:** a etapa não toca. Os campos ficam vazios e a origem nula.

### Leitura do Graph

- **Porta nova em `IEntraGroupDirectory`.** Ela lê os membros transitivos usuários do grupo de provisionamento com `$select=id,jobTitle,department,businessPhones` (**lista fixa**, nada vindo do chamador) e devolve, por pessoa, os três textos e o `oid` do gestor (ou nenhum).
- **Telefone:** o primeiro item de `businessPhones`. O `telephoneNumber` do AD local chega ao Graph exatamente por esse campo, e o Graph v1 não tem outro.
- **Gestor:** a primeira opção é `$expand=manager($select=id)` na mesma leitura. **É preciso confirmar contra um tenant Entra real** se o `$expand` funciona em `transitiveMembers/microsoft.graph.user` e com qual `$top`. Se não funcionar, o gestor vem de `users/{oid}/manager?$select=id` por pessoa, preferencialmente agrupado em `$batch` de 20. Nesse caminho, 404 significa "sem gestor" e não é falha.
- **Tudo ou nada**, como a ADR-0055: paginação inteira com teto de páginas, cursor só do próprio Graph (SSRF), e qualquer erro, resposta malformada ou lote de gestor que falhe faz a leitura inteira falhar. **Leitura falha não muda nada** no tenant naquele ciclo.
- **Sem consentimento** (401/403): a etapa é pulada naquele tenant, com aviso no log. Contas e perfis já foram reconciliados antes e não são afetados.

### Ordem no ciclo

Dentro de `ReconcileTenantAsync`, **depois** de contas e perfis (passo 4):

1. A etapa roda só se o tenant tem grupo de provisionamento e o ciclo **não** foi abortado por freio nem por falha de leitura.
2. Relista as contas vinculadas, para alcançar as criadas neste mesmo ciclo.
3. Lê os atributos, resolve os gestores, aplica o freio de esvaziamento (abaixo) e grava só o que mudou.
4. Uma exceção ou falha nessa etapa não muda o desfecho das etapas anteriores, que já foram aplicadas e registradas.

### Validação dos textos (ADR-0020)

Os textos vêm do diretório do cliente, então são input externo:

- **Normalização:** `Trim`. Texto vazio vira nulo.
- **Caractere de controle** (`char.IsControl`, incluindo quebras de linha) **descarta o campo**, que fica nulo, e a conta segue. Isso não falha a leitura: um cargo malformado não deve parar a sincronização da empresa.
- **Tamanhos máximos:** cargo 128, departamento 128, telefone 64. Texto acima do limite é descartado (nulo), não truncado: truncar grava um valor que ninguém escreveu.
- O conteúdo nunca vai para o log. O log leva só ids e contagens.

### Gestor

O `oid` do gestor vira referência a uma conta **deste tenant** (`ManagerId`, um `Guid?`):

| Situação | Resultado |
| --- | --- |
| Sem gestor no Entra | nulo |
| Gestor é conta dona deste tenant, vinculada ao mesmo diretório por `oid`, ativa | `ManagerId` = id dessa conta |
| Gestor sem conta no tenant (fora do grupo de provisionamento, outro tenant, nunca provisionado) | nulo + aviso |
| Gestor com conta desativada ou bloqueada | nulo + aviso |
| Gestor é a própria pessoa | nulo + aviso |
| Ciclo (A → B → … → A), resolvido sobre o grafo do tenant depois da resolução | gestor nulo em **todas** as contas do ciclo + aviso |

- **Ciclo:** quebrar o ciclo inteiro, e não uma aresta escolhida, evita eleger arbitrariamente um "topo". É erro de dado no Entra e o TI do cliente corrige. O grafo considerado é o de origem `directory` do tenant, que é o único que tem gestor.
- **Gestor desativado vira nulo** porque um organograma com gestor inexistente engana. Quando o gestor é reativado, ou o Entra aponta outro, a rodada seguinte corrige.
- **Aviso no log, sem trilha.** Atributo de cadastro não é ato de autoridade (ADR-0054). O ciclo emite um aviso **por tenant**, com as contagens por motivo (`manager-not-found`, `manager-inactive`, `manager-self`, `manager-cycle`, `field-discarded`). O detalhe por conta (id SecureGate, nunca e-mail nem texto) sai em `Debug`, para não inundar o log numa empresa grande.

### Freio de esvaziamento

A permissão menor pode fazer o Graph devolver propriedades nulas em vez de 403, e isso não está confirmado. Uma leitura assim apagaria os campos de todos. Por isso, **se a rodada fosse limpar algum campo que hoje tem valor (de "tem valor" para nulo) em mais contas que o piso e a fração do freio da ADR-0055** (`MinRemovalsBeforeBrake` e `MaxRemovalRatio`, sobre as contas geridas), a etapa é abortada naquele tenant, nada é gravado e o log avisa. Preencher e alterar valores não passam pelo freio.

### Armazenamento

Colunas em `tb_users` (`User`), por convenção ADR-0017:

| Propriedade | Coluna | Tipo |
| --- | --- | --- |
| `JobTitle` | `ds_job_title` | texto ≤ 128, nulo |
| `Department` | `ds_department` | texto ≤ 128, nulo |
| `BusinessPhone` | `ds_business_phone` | texto ≤ 64, nulo |
| `ManagerId` | `id_fk_manager` | `Guid?`, auto-referência a `tb_users` |
| `AttributesOrigin` | `ie_attributes_origin` | enum `None`/`Directory`, padrão `None` |

- **Migration nos dois engines** (SQL Server e Postgres). A `tb_users` está sob RLS (ADR-0041 rodada 2), então a migration usa `TenantRowLevelSecurity.Drop/Create` com a revisão vigente do helper.
- **A auto-referência fica sem cascade.** O SQL Server recusa `SET NULL` em FK auto-referente. Hoje não existe exclusão de conta no SecureGate (conferido em 2026-11-17: conta só é desativada), então nenhuma referência fica órfã. Se a exclusão nascer um dia, ela limpa antes as referências de gestor.
- A primeira gravação pela etapa marca `AttributesOrigin = Directory`. A origem nunca volta a `None` sozinha nesta entrega.

### API e Client

`DirectoryPersonDto` (`GET /api/v1/directory/people` e `.../{userId}`) ganha:

- `jobTitle`, `department`, `businessPhone`: `string?`
- `managerId`: `Guid?`. É exposto **só se o gestor também aparece no diretório do tenant da requisição** (dono ou membro com vínculo ativo, ADR-0045). Caso contrário sai nulo. Assim, um tenant B que tem a pessoa como membro não fica sabendo de uma conta do tenant A que ele não enxerga.
- `attributesSource`: `string?`, `"directory"` ou nulo. É **string no OpenAPI** (não enum C# sem `type: string`, o defeito que já quebrou o client do LogStream).

O contrato só cresce. `openapi.json` regenerado e `Secco.SecureGate.Client` em **0.25.0** (minor). `operationId`s existentes intocados.

### Consentimento e guia de instalação

`src/SecureGate/README.md`, seção de federação e provisionamento:

- **Permissão de aplicação nova:** `User.Read.All`, com consentimento de admin no diretório do cliente. Ela inclui o que o `User.ReadBasic.All` dava, mas manter as duas não faz mal.
- **Ela permite ler mais do que usamos.** O SecureGate pede só a lista fixa de campos, mas a permissão concedida vale para todo o perfil das pessoas do diretório. O guia diz isso com todas as letras.
- **O que muda para quem já usa:** nada quebra. Sem o consentimento novo, provisionamento e perfis seguem iguais, os quatro campos ficam vazios e o log avisa a cada ciclo. Para ligar, o admin do cliente concede o `User.Read.All` e o próximo ciclo preenche.
- O Graph falso dos testes valida o formato da chamada, não o comportamento do serviço. **Antes de ligar em produção, confirmar contra um tenant Entra de teste:** o `$expand=manager` em `transitiveMembers`, e o comportamento de selecionar `jobTitle` com só `User.ReadBasic.All` (403 ou nulos).

Também na entrega: ADR-0059, `CHANGELOG.md`, `docs/roadmap.md`, `docs/design-decisions-log.md` (alternativas descartadas acima) e o resumo no `CLAUDE.md`.

## Segurança (ADR-0020)

- **Input externo:** textos validados (controle, tamanho) antes de persistir. O gestor entra só como `Guid` resolvido para uma conta do tenant, nunca como texto. O `$select` é fixo e os ids entram na URL formatados como `Guid`.
- **Isolamento de tenant:** a etapa roda no escopo do tenant (ADR-0041). Gestor só resolve para conta do mesmo tenant, e o `managerId` na API só aparece se for visível no tenant da requisição.
- **Vazamento:** telefone e cargo são dado pessoal exposto a clients `directory:read` do próprio tenant, que é a função pedida (risco aceito, como na ADR-0045). O log não leva conteúdo, só ids e contagens.
- **Negação de serviço / custo:** a leitura do gestor por pessoa é limitada pelo teto de páginas, pelo `$batch` e pela frequência do ciclo (24 h). O login não ganha chamada nova.
- **Privilégio:** `User.Read.All` é mais amplo que o necessário. O risco é documentado e o uso fica restrito por `$select` fixo.
- **Dependências:** nenhuma nova.

## Testes esperados

- Leitura do Graph (fake HTTP): `$select` exato, paginação, cursor fora do Graph recusado, resposta malformada = falha, 403 = sem consentimento, gestor por `$expand` e/ou por `/manager` (404 = sem gestor, outro erro = falha total).
- Regras do gestor: cada linha da tabela, incluindo ciclo de 2 e de 3, e gestor visível ou não no tenant da requisição (ADR-0040).
- Validação: controle, tamanho, vazio, campo descartado sem derrubar a conta.
- Freio de esvaziamento: dispara acima do piso e da fração; preencher e alterar não contam.
- Integração (Testcontainers, os dois engines): migration com RLS, a etapa atualiza na rodada seguinte, sem consentimento contas e perfis seguem, e falha da etapa não desfaz contas nem perfis.
- API de diretório: campos e `attributesSource` presentes, `null` para conta local.
- Mutação nos invariantes de segurança (isolamento do `managerId`, freio, fail-closed da leitura, validação), como nas entregas anteriores.

## Deixado ao construtor

- Implementar o gestor com `$expand` ou com `/manager` + `$batch`, conforme a confirmação contra tenant real. A porta é a mesma nos dois casos.
- Nomes de tipos, EventIds de log e a forma do resultado da etapa no `DirectorySyncResult`.
