# Provisionamento de banco de tenant — design

**Data:** 2026-09-05
**Status:** aprovado para planejamento
**Item de origem:** issue [#3](https://github.com/rafsecco/secco-platform/issues/3) (`adopter-demand`) —
não existe provisionamento de banco de tenant na plataforma: nem criação do database, nem criação
do usuário de banco, nem concessão de permissão. Destravada em 2026-09-04 pela decisão da
[#4](https://github.com/rafsecco/secco-platform/issues/4), que definiu o dono.

## Contexto

O único `CREATE DATABASE` do monorepo está em `Secco.SDK.Testing/SeccoSqlServerInstance.cs` —
infraestrutura de teste, não caminho de produção. O AdminPortal cadastra ou rotaciona a connection
string de um par (tenant, produto), mas **pressupõe um banco que já existe, criado por fora**. E os
exemplos de adoção usam `User Id=sa`, que é o que o adotante seguinte copia.

Enquanto a connection string de um tenant usar um usuário amplo, o isolamento físico da ADR-0005 é
**convenção, não garantia**: um erro de connection string alcança o banco do tenant vizinho, e a
aplicação tem no servidor todo o poder que aquele usuário tem.

### Quem é o dono, e por que não é o portal

A ADR-0007 do `secco-intranet` já decidiu: criar database, criar login e conceder permissão "são
capacidades da plataforma, que custodia o catálogo cifrado (ADR-0025 da plataforma) e é o único
lugar onde uma credencial privilegiada de banco deve existir". O provisionamento vive no
**SecureGate** — que já é o registro de (tenant, produto) → banco — e qual UI o chama volta a ser
detalhe reversível (decisão da #4, 2026-09-04).

### Dois cenários, dois privilégios

A issue nomeia duas situações reais de adoção, e tratá-las como uma força o pior privilégio nas duas:

| Cenário | O que falta | Privilégio necessário |
| --- | --- | --- |
| O banco **já existe** (comum em adoção corporativa) | login + usuário + concessão naquele banco | criar login no servidor; conceder dentro de um banco |
| **Tenant novo** entra | o database, e depois o acima | o acima **+** criar database |

## Decisões desta rodada

1. **Dois modos, mesmos artefatos.** O modo **script** está sempre disponível e é o default: o
   SecureGate gera o SQL correto e um DBA aplica. O modo **automático** é opt-in por configuração:
   havendo credencial privilegiada declarada, o próprio SecureGate executa. Sem a seção, a
   automação **não existe** — fail-closed, não degradação silenciosa. Motivo: muitos DBAs
   corporativos jamais concedem `dbcreator` a uma aplicação, e um recurso que só funciona com esse
   privilégio simplesmente não serve a esses adotantes; já o script serve, e serve correto e com
   privilégio mínimo por construção.
2. **Execução síncrona, não job em background.** Considerado e descartado usar Hangfire (que o
   SecureGate não tem hoje). A assincronia não entrega a segurança que aparentava: a credencial
   fica no mesmo processo de qualquer forma — o job move *quando* ela é usada, não *onde* mora. O
   que protege é opt-in + gate `securegate:admin` + conexão isolada usada só para isto. Criar
   database leva segundos, e retry automático de DDL é mais perigoso que útil (repetir uma criação
   parcial é pior que um erro visível). Somar Hangfire custaria banco de plataforma novo e mais uma
   peça para o adotante operar, em troca de uma garantia que não se realiza.
3. **SQL Server primeiro; PostgreSQL depois, com a abstração pronta.** O DDL diverge muito entre os
   dois (`CREATE LOGIN` + usuário no banco vs `CREATE ROLE` + `GRANT`), e cada um tem armadilhas
   próprias de identificador e de permissão. Esta é a parte mais perigosa do sistema: um engine bem
   feito e provado vale mais que dois pela metade. A abstração nasce com dois implementadores
   previstos e um entregue.
4. **O painel de bancos não usa credencial privilegiada.** Observação do próprio adotante na issue,
   e ela se sustenta: o catálogo já sabe tenant e banco, e o estado vivo sai de um health-check por
   banco usando a **conexão de runtime** de cada tenant.
5. **Sem rotação de senha nesta entrega.** É uma terceira operação privilegiada e merece rodada
   própria. Reprovisionar um par (tenant, produto) que já existe responde **409**, nunca sobrescreve
   em silêncio.

## Arquitetura

### Superfície HTTP

```
POST /api/v1/tenants/{id}/databases/{product}/provisioning     securegate:admin
GET  /api/v1/tenants/{id}/databases/status                     securegate:admin
```

**Requisição de provisionamento:**

```jsonc
{
  "target": "default",        // alvo declarado em configuração; opcional
  "createDatabase": true,     // false = o banco já existe, só criar login/usuário/grants
  "databaseName": null,       // opcional; default derivado do slug do tenant + produto
  "loginName": null           // opcional; default derivado
}
```

**Resposta 200:**

```jsonc
{
  "applied": false,           // true = o SecureGate executou; false = aplique o script
  "databaseName": "secco_logstream_contoso",
  "loginName": "secco_logstream_contoso_app",
  "script": "CREATE DATABASE ..."   // presente SOMENTE quando applied = false
}
```

Em **ambos** os modos a connection string resultante é gravada cifrada no catálogo (ADR-0025) e
**nunca** volta na resposta — a disciplina write-only da 6.3 continua valendo. O `script` é a única
exceção e é deliberada: ele contém a senha porque quem o aplica precisa dela. É devolvido **uma
única vez**, não é persistido em claro e não é recuperável depois.

### Componentes

| Tipo | Camada | Responsabilidade |
| --- | --- | --- |
| `ProvisioningTarget` | Application (options) | Alvo declarado em configuração: provider, servidor, credencial privilegiada opcional |
| `ITenantDatabaseProvisioner` | Application | Contrato por engine: gera o script e (se puder) executa |
| `SqlServerTenantDatabaseProvisioner` | Infrastructure | Implementação SQL Server |
| `DatabaseIdentifierPolicy` | Application | Validação de nome de database/login por allowlist |
| `ProvisionTenantDatabaseHandler` | Application | Orquestra: valida, gera, executa ou não, grava no catálogo |
| `ITenantDatabaseHealthProbe` | Application | `SELECT 1` na conexão de runtime, para o painel |

O seletor por receita segue o padrão que a ADR-0027 fixou (`SeccoDatabaseProviders`): o SecureGate
declara os provisionadores que aceita, sem que o SDK conheça engine.

### Configuração

```jsonc
"SecureGate": {
  "Provisioning": {
    "Targets": {
      "default": {
        "Provider": "SqlServer",
        "Server": "sql-01.interno,1433",
        // Ausente = SOMENTE modo script. Presente = automação ligada. Nunca logada.
        "AdminConnectionString": "Server=...;User Id=provisioner;Password=..."
      }
    }
  }
}
```

Seção ausente por completo: o endpoint segue funcionando em modo script, mas exige `Server` na
requisição — sem isso não há como montar a connection string do tenant. Configuração **parcial**
falha no startup (fail-fast, mesma postura de `SecureGateClientCredentialsOptions`).

### O que é concedido

O usuário criado recebe **`db_owner` no banco dele e nada além** — necessário porque cada produto
roda as próprias migrations do EF Core, que exigem DDL. No servidor, nenhum papel. É a diferença
entre "um erro de connection string alcança o banco do vizinho" e "um erro de connection string não
alcança nada".

## Segurança (ADR-0020)

Esta é a entrega mais perigosa da plataforma até aqui. O que a governa:

| Vetor | Tratamento |
| --- | --- |
| **Injeção de SQL por identificador** | Nome de database e de login **não podem ser parametrizados** em DDL. Allowlist estrita (`^[a-z][a-z0-9_]{2,62}$`) aplicada **antes** de qualquer concatenação, mais delimitação por `[ ]` com escape de `]`. Mesma disciplina que o `SeccoSqlServerInstance` já usa para `CREATE`/`DROP DATABASE` em teste |
| **Credencial privilegiada** | Opt-in; ausente = recurso indisponível. Usada por uma conexão criada só para a operação e descartada em seguida; nunca compartilhada com o `DbContext` da aplicação. Nunca logada, nunca em mensagem de erro, nunca em resposta |
| **Segredo gerado** | Senha aleatória criptográfica (`RandomNumberGenerator`), ≥ 32 caracteres, de um alfabeto que não quebra connection string (sem `;`, `'`, `"`, `=`). Devolvida uma vez no script; persistida apenas cifrada |
| **Vazamento por erro** | Falha de provisionamento retorna classificação (`servidor inacessível`, `sem permissão`, `banco já existe`), nunca a exceção crua nem a connection string |
| **Escalada por reprovisionamento** | Par (tenant, produto) já existente responde **409**. Sem sobrescrita silenciosa, sem reset de senha por acidente |
| **Isolamento de tenant** | O banco criado é de um tenant só; o grant é escopado àquele banco. É justamente esta entrega que transforma a ADR-0005 de convenção em garantia |
| **Autorização** | `securegate:admin` nos dois endpoints, como toda a gestão da 6.3 |
| **Negação de serviço** | Provisionamento é operação de operador, não de usuário; o gate de escopo já limita. O health-check do painel tem timeout curto por banco, para um banco fora do ar não pendurar a página |

## Escopo

**Dentro:** os dois endpoints, o provisionador SQL Server nos dois modos, a política de
identificadores, geração de segredo, gravação cifrada no catálogo, painel de status, ADR-0028,
testes, e a atualização do `docker-compose`/`.env.example` para deixar de ensinar `sa`.

**Fora (registrado):** PostgreSQL (rodada seguinte, abstração já pronta); rotação de senha de banco;
remoção/desprovisionamento de tenant; aplicação de migrations pelo SecureGate (cada produto roda as
suas); tela no AdminPortal (a API primeiro).

## Testes

Unit, sem infraestrutura:

- `Identifier_WhenNameHasSemicolonOrBracket_IsRejected` e variantes — a barreira de injeção.
- `Identifier_WhenNameIsReservedOrTooLong_IsRejected`.
- `Password_IsGeneratedWithSafeAlphabetAndMinimumLength`.
- `Script_WhenCreateDatabaseIsFalse_OmitsCreateDatabase`.
- `Provision_WhenAutomationNotConfigured_ReturnsScriptAndAppliedFalse`.
- `Provision_WhenTargetPartiallyConfigured_FailsFast`.

Integração com Testcontainers (SQL Server real — é o único jeito honesto de testar DDL):

- `Provision_WhenAutomationEnabled_CreatesDatabaseLoginAndUser`.
- `Provision_WhenCreated_StoresEncryptedConnectionStringAndNeverReturnsIt`.
- `Provision_WhenAlreadyProvisioned_Returns409`.
- **`ProvisionedUser_CannotReachAnotherTenantDatabase`** — o teste que dá sentido à entrega inteira:
  conectar com o usuário criado e provar que o banco do vizinho é inalcançável.
- `Status_WhenDatabaseUnreachable_ReportsUnreachableWithoutLeakingConnectionString`.

## ADR-0028

A decisão afeta 2+ produtos e é difícil de reverter — ADR nova, curta, cobrindo: o provisionamento é
capacidade da plataforma e vive no SecureGate; dois modos com automação opt-in fail-closed;
`db_owner` no próprio banco como teto do que se concede; identificadores por allowlist; segredo
gerado no servidor, exibido uma vez, persistido só cifrado.
