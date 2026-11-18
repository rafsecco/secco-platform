# Atributos de pessoa vindos do diretório federado — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** a sincronização periódica do SecureGate (ADR-0055) grava cargo, departamento, telefone comercial e gestor do Entra nas contas geridas pelo diretório, e a API `directory:read` (ADR-0045) os expõe com a origem `directory`.

**Architecture:** etapa própria no fim de `DirectoryRoleReconciler.ReconcileTenantAsync`, depois de contas e perfis, com uma leitura separada do Graph. Sem `User.Read.All`, o 403 pula só essa etapa. O planejamento é puro (Application): valida os textos, resolve o gestor, quebra ciclos e aplica o freio de esvaziamento. A gravação vai para cinco colunas novas em `tb_users`. O `DirectoryReader` projeta os campos e só mostra o `managerId` quando o gestor é visível no tenant da requisição.

**Tech Stack:** .NET 10, EF Core (SQL Server + Postgres), ASP.NET Core Minimal APIs, NSwag, xUnit + FluentAssertions 7 + NSubstitute + Testcontainers.

**Spec:** [docs/superpowers/specs/2026-11-17-atributos-do-diretorio-design.md](../specs/2026-11-17-atributos-do-diretorio-design.md)

## Global Constraints

- Tamanhos máximos: cargo **128**, departamento **128**, telefone **64**. Texto acima do limite é **descartado** (nulo), nunca truncado.
- Texto com qualquer `char.IsControl` é descartado. `Trim`; vazio vira nulo.
- `$select` fixo do Graph: `id,jobTitle,department,businessPhones`. Gestor: `users/{oid:D}/manager?$select=id` por pessoa, sequencial. Esse endpoint devolve **404 = sem gestor**; qualquer outra falha derruba a leitura inteira.
- Freio: usa `DirectorySyncOptions.EffectiveMinRemovalsBeforeBrake` e `EffectiveMaxRemovalRatio` (padrões 5 e 0,10) sobre o total de contas geridas do tenant.
- Origem no contrato: `attributesSource` é `string?` com o valor `"directory"` ou nulo. **Nunca** enum C# no DTO.
- Log sem conteúdo do diretório: só ids SecureGate, ids de tenant e contagens (ADR-0020).
- Atributo não vai à trilha de autoridade (ADR-0054).
- O nome (`DisplayName`) **não** é tocado pela etapa.
- Colunas (ADR-0017, pela convenção): `ds_job_title`, `ds_department`, `ds_business_phone`, `id_fk_manager`, `ie_attributes_origin`. Migration com `TenantRowLevelSecurity.Drop`/`Create(..., RevisionAdr0054)` nos dois engines.
- FK `id_fk_manager` → `tb_users` com `DeleteBehavior.NoAction` (o SQL Server recusa `SET NULL` auto-referente). Não existe exclusão de conta.
- `Secco.SecureGate.Client` vai a **0.25.0** (minor). A publicação segue a skill `secco-platform-release`, à parte e só com autorização do usuário.
- Commits direto na `main`, Conventional Commits, terminando com `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Verificação final pelo comando documentado: `dotnet build Secco.Platform.slnx -c Release` e `dotnet test Secco.Platform.slnx`.

## Review Focus

1. **`businessPhones` vazio, com string vazia ou com só espaços** → telefone nulo, sem falha. Pinado na Task 2 (`Atributos_TelefoneListaVaziaOuAusente_ViraNulo`) e na Task 4 (`Plan_TelefoneEmBranco_ViraNulo`).
2. **Gestor que não é usuário** (contato organizacional; o `/manager` devolve um `directoryObject` com id que não casa com conta nenhuma) → `ManagerNotFound`, nunca falha da leitura. Pinado na Task 4 (`Gestor_OidSemContaNoTenant_NuloComAviso`); na Task 2, o parser só exige `id` Guid.
3. **Um único 429/5xx no meio das N chamadas de gestor** → a leitura inteira falha e nada é gravado. Pinado na Task 2 (`Atributos_UmGestorFalha_LeituraInteiraFalha`).
4. **Conta criada neste mesmo ciclo** recebe os atributos no mesmo ciclo. Pinado na Task 5 (`Ciclo_ContaCriadaNoCiclo_JaRecebeAtributos`).
5. **Gestor que ficou inativo sem nada mudar no Entra** → a rodada seguinte zera o `ManagerId` já gravado, porque a resolução é refeita sempre e não só quando o Graph muda. Pinado na Task 4 (`Gestor_JaGravadoQueFicouInativo_ZeraNaProximaRodada`).

---

### Task 1: Regras de validação dos atributos

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Federation/DirectoryAttributes.cs`
- Create: `src/SecureGate/Secco.SecureGate.Application/Federation/DirectoryAttributeRules.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/DirectoryAttributeRulesTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record DirectoryAttributes(Guid ObjectId, string? JobTitle, string? Department, string? BusinessPhone, Guid? ManagerObjectId)`. Valores **crus** do Graph, input externo, com o doc-comment no estilo de `DirectoryPerson`.
  - `public static class DirectoryAttributeRules` com `JobTitleMaxLength = 128`, `DepartmentMaxLength = 128`, `BusinessPhoneMaxLength = 64` e `public static string? Normalize(string? raw, int maxLength, out bool discarded)`.

- [ ] **Step 1: Testes que falham** (`DirectoryAttributeRulesTests`):
  - `Normalize(null, 128, out d)` → `null`, `d == false`
  - `Normalize("   ", 128, out d)` → `null`, `d == false`
  - `Normalize("  Analista  ", 128, out d)` → `"Analista"`, `d == false`
  - `Normalize(new string('a', 128), 128, out d)` → as 128 letras, `d == false`
  - `Normalize(new string('a', 129), 128, out d)` → `null`, `d == true`
  - `[Theory]` com `"Ana\nlista"`, `"Ana\rlista"`, `"Ana\u0000"`, `"Ana\u001b[31m"`, `"Ana\u0085"` → `null`, `d == true`
  - o limite vale **depois** do `Trim`: `" " + new string('a', 64) + " "` com 64 → aceito
- [ ] **Step 2:** `dotnet test tests/SecureGate/Secco.SecureGate.Tests -c Release --filter FullyQualifiedName~DirectoryAttributeRulesTests` → falha de compilação.
- [ ] **Step 3:** implementar o record e a classe de regras.
- [ ] **Step 4:** mesmo comando → verde.
- [ ] **Step 5:** commit `feat(securegate): regras de validacao dos atributos do diretorio`.

---

### Task 2: Leitura dos atributos no Graph

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Federation/IEntraGroupDirectory.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Federation/GraphGroupDirectory.cs`
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Integration/FakeEntraGroupDirectory.cs` (implementar o membro novo: dicionário por grupo, mais um modo "proibido" para simular 403)
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Unit/DirectoryReconcilerRig.cs`: no construtor, `GetGroupMemberAttributesAsync` devolve por padrão **sucesso com lista vazia**, senão o NSubstitute devolve `null` e quebra os testes atuais com grupo de provisionamento. O mesmo padrão vale no `FakeEntraGroupDirectory`. Mais os helpers `GraphAttributes(groupId, params DirectoryAttributes[])` e `GraphAttributesFail(groupId, Error)`.
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/GraphDirectoryAttributesTests.cs` (mesmo `ScriptedHandler`/`Build` de `GraphDirectoryMembershipTests`)

**Interfaces:**
- Consumes: `DirectoryAttributes` (Task 1).
- Produces: `Task<Result<IReadOnlyList<DirectoryAttributes>>> GetGroupMemberAttributesAsync(Guid directoryId, Guid groupId, CancellationToken cancellationToken = default)` em `IEntraGroupDirectory`.

- [ ] **Step 1: Testes que falham:**
  - `Atributos_MontaAUrlComSelectFixo` → primeira URL exatamente `https://graph.microsoft.com/v1.0/groups/{GroupId:D}/transitiveMembers/microsoft.graph.user?$select=id,jobTitle,department,businessPhones&$top=999`.
  - `Atributos_LeGestorPorPessoa` → para cada membro, uma requisição a `.../users/{oid:D}/manager?$select=id`; resposta `{"id":"<guid>"}` vira `ManagerObjectId`.
  - `Atributos_GestorAusente404_ViraNulo` → `ManagerObjectId == null`, resultado sucesso.
  - `Atributos_UmGestorFalha_LeituraInteiraFalha` → um `/manager` responde 429 → `IsFailure`, código `SecureGate.Federation.DirectoryUnavailable`.
  - `Atributos_Sem403Consentimento_ConsentRequired` → membros 403 → `SecureGate.Federation.ConsentRequired`, nenhuma chamada a `/manager`.
  - `Atributos_PaginacaoENextLinkForaDoGraph` → percorre duas páginas, e `@odata.nextLink` para outro host dá `DirectoryReadIncomplete` sem requisição a ele.
  - `Atributos_TelefoneListaVaziaOuAusente_ViraNulo` → `businessPhones: []`, `null` ou ausente → `BusinessPhone == null`. Com `[""]`, o parser devolve `""` cru; quem transforma em nulo é a Task 4.
  - `Atributos_PrimeiroTelefone` → `["+55 11 1111","+55 11 2222"]` → `"+55 11 1111"`.
  - `[Theory] Atributos_RespostaMalformada_LeituraIncompleta` → `jobTitle: 1`, `businessPhones: "x"` (não array), `businessPhones: [1]`, membro sem `id`, `/manager` com `id` não Guid.
  - `Atributos_GestorComIdDeContatoQualquer_DevolveOOid` → `/manager` devolve `{"@odata.type":"#microsoft.graph.orgContact","id":"<guid>"}` → sucesso com aquele Guid.
- [ ] **Step 2:** `dotnet test ... --filter FullyQualifiedName~GraphDirectoryAttributesTests` → falha.
- [ ] **Step 3: Implementar.**
  - Tornar `ReadAllMembersAsync` genérico sobre o item (chave = `ObjectId`), com o mesmo teto de páginas, o `TryAdd` por id e a checagem do cursor. Os dois chamadores atuais continuam iguais.
  - Parser novo `ParseMemberAttributesPage`, que lê os textos com o `TryReadText` existente.
  - Depois da leitura completa, uma chamada `SendAsync` por pessoa ao `/manager`. `DirectoryObjectNotFound` (404) vira `null`; qualquer outra falha devolve `Result.Failure` na hora.
  - Constante `AttributesSelect = "id,jobTitle,department,businessPhones"`, ao lado de `ProfileSelect`.
- [ ] **Step 4:** mesmo filtro, mais `GraphDirectoryMembershipTests` (regressão da generalização) → verdes.
- [ ] **Step 5:** commit `feat(securegate): leitura de cargo, departamento, telefone e gestor no Graph`.

---

### Task 3: Colunas, migrations e store

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Users/DirectoryAttributesOrigin.cs`. Enum `None = 0`, `Directory = 1`.
- Create: `src/SecureGate/Secco.SecureGate.Application/Federation/IDirectoryAttributeStore.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Identity/IdentityEntities.cs` (`User`: `JobTitle`, `Department`, `BusinessPhone`, `ManagerId`, `AttributesOrigin`)
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Contexts/SecureGateDbContext.cs` (no bloco `Entity<User>`: `HasMaxLength` com as constantes da Task 1, `HasOne<User>().WithMany().HasForeignKey(u => u.ManagerId).OnDelete(DeleteBehavior.NoAction)`, índice `idx_users_id_fk_manager`)
- Create: `src/SecureGate/Secco.SecureGate.Infrastructure/Federation/DirectoryAttributeStore.cs` + registro no DI, junto de `DirectoryAccountStore`
- Create: migrations `AddDirectoryAttributes` em `Secco.SecureGate.Migrations.SqlServer` e `Secco.SecureGate.Migrations.Postgres`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/DirectoryAttributeStoreTests.cs` (`[Collection(SharedApiCollectionDefinition.Name)]`) e `Integration/PostgresDirectoryAttributesTests.cs` (`IClassFixture<PostgresRlsFixture>`, no molde de `PostgresDirectoryProvisioningTests`)

**Interfaces:**
- Consumes: `DirectoryAttributeRules` (Task 1).
- Produces:
  - `public sealed record DirectoryAttributeAccount(Guid UserId, Guid ObjectId, bool DirectoryManaged, bool IsActive, string? JobTitle, string? Department, string? BusinessPhone, Guid? ManagerId, DirectoryAttributesOrigin Origin)`
  - `public sealed record DirectoryAttributeUpdate(Guid UserId, string? JobTitle, string? Department, string? BusinessPhone, Guid? ManagerId)`
  - `IDirectoryAttributeStore`:
    - `Task<IReadOnlyList<DirectoryAttributeAccount>> ListLinkedAsync(Guid tenantId, Guid directoryId, CancellationToken cancellationToken = default)`: contas **donas** do tenant vinculadas a `{tid}:{oid}`, com o mesmo critério de vínculo de `DirectoryRoleStore.ListLinkedAccountsAsync`. `IsActive` = `UserStatuses.From(...) == UserStatuses.Active`.
    - `Task<int> ApplyAsync(Guid tenantId, IReadOnlyList<DirectoryAttributeUpdate> updates, CancellationToken cancellationToken = default)`: grava os quatro campos e `AttributesOrigin = Directory` só em conta dona do tenant; devolve quantas gravou.

- [ ] **Step 1: Testes que falham:**
  - `ListLinked_SoDonasVinculadasAoDiretorio` → conta de outro diretório, conta só local e membro por vínculo (ADR-0040) ficam fora.
  - `ListLinked_ContaDesativada_IsActiveFalse`.
  - `Apply_GravaCamposEMarcaOrigem_ENaoTocaNoNome` → `DisplayName` intacto.
  - `Apply_ContaDeOutroTenant_NaoGrava` → devolve 0.
  - `Postgres`: colunas com tipo e tamanho (`[Theory]` com as cinco), round-trip pelo EF com RLS ligado e vizinho sem enxergar.
  - A suíte de schema existente (`Migrations_Always_ApplyFromScratch` e semelhantes) cobre a migration a partir do zero.
- [ ] **Step 2:** rodar o filtro `~DirectoryAttributeStoreTests|~PostgresDirectoryAttributesTests` → falha.
- [ ] **Step 3: Implementar entidade, configuração e store, e gerar as migrations:**

```bash
dotnet ef migrations add AddDirectoryAttributes --project src/SecureGate/Secco.SecureGate.Migrations.SqlServer --output-dir Migrations
dotnet ef migrations add AddDirectoryAttributes --project src/SecureGate/Secco.SecureGate.Migrations.Postgres --output-dir Migrations
```

  Editar o `Up`/`Down` gerado nos dois engines: `TenantRowLevelSecurity.Drop(migrationBuilder)` antes e `TenantRowLevelSecurity.Create(migrationBuilder, TenantRowLevelSecurity.RevisionAdr0054)` depois, como em `20261031132408_AddImplicitTenantRole`. Conferir que os nomes saíram pela convenção, sem digitar à mão.
- [ ] **Step 4:** filtro da Task 3 + `~PlatformSchemaTests|~PostgresParityTests` → verdes.
- [ ] **Step 5:** commit `feat(securegate): colunas dos atributos do diretorio em tb_users`.

---

### Task 4: Planejamento — gestor, ciclos e freio

**Files:**
- Create: `src/SecureGate/Secco.SecureGate.Application/Federation/DirectoryAttributePlanner.cs`
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/DirectoryAttributePlannerTests.cs`

**Interfaces:**
- Consumes: `DirectoryAttributes`, `DirectoryAttributeRules` (Task 1); `DirectoryAttributeAccount`, `DirectoryAttributeUpdate` (Task 3); `DirectorySyncOptions`.
- Produces:
  - `public enum DirectoryAttributeWarning { ManagerNotFound, ManagerInactive, ManagerSelf, ManagerCycle, FieldDiscarded }`
  - `public sealed record DirectoryAttributeNotice(Guid UserId, DirectoryAttributeWarning Warning)`
  - `public sealed record DirectoryAttributePlan(IReadOnlyList<DirectoryAttributeUpdate> Updates, IReadOnlyList<DirectoryAttributeNotice> Notices, int Cleared, int ManagedTotal)`
  - `public static class DirectoryAttributePlanner`:
    - `public static DirectoryAttributePlan Plan(IReadOnlyList<DirectoryAttributeAccount> linked, IReadOnlyList<DirectoryAttributes> read)`
    - `public static bool ExceedsBrake(DirectoryAttributePlan plan, DirectorySyncOptions options)`

**Regras que o código precisa seguir:**

- **Alvos:** contas de `linked` com `DirectoryManaged` cujo `ObjectId` está em `read`.
- **Textos:** passam pelo `Normalize`. Cada campo descartado gera um `FieldDiscarded`, no máximo um aviso por conta.
- **Resolução do gestor:**
  - `ManagerObjectId` nulo → `null`, sem aviso.
  - Sem conta em `linked` com aquele `ObjectId` → `ManagerNotFound`.
  - É a própria conta → `ManagerSelf`.
  - Conta `!IsActive` → `ManagerInactive`.
  - Senão, o `UserId` dela.
- **Ciclo:** grafo com as arestas desejadas dos alvos mais o `ManagerId` atual das contas que não são alvo. Todo alvo que está num ciclo fica com gestor `null` e recebe `ManagerCycle`. Algoritmo:

```text
para cada alvo a: percorre a → gestor(a) → gestor(gestor(a)) …, guardando os visitados deste caminho;
se voltar a um nó do caminho, os nós do ciclo (do ponto de reentrada em diante) são marcados.
(Memoize "sem ciclo" por nó já resolvido para ficar O(n).)
```

- **Update:** só quando algum dos quatro valores desejados difere do atual **ou** `Origin != Directory`.
- **`Cleared`:** alvos em que algum texto atual não nulo tem o valor **cru do Graph** nulo ou em branco, ou em que o `ManagerId` atual não é nulo e o `ManagerObjectId` lido é nulo. Nulos vindos de descarte ou de resolução **não** contam: o freio protege contra leitura ruim, não contra decisão nossa.
- **`ManagedTotal`:** `linked.Count(a => a.DirectoryManaged)`.
- **`ExceedsBrake`:** `Cleared > options.EffectiveMinRemovalsBeforeBrake && ManagedTotal > 0 && (double)Cleared / ManagedTotal > options.EffectiveMaxRemovalRatio`.

- [ ] **Step 1: Testes que falham:** um por linha da tabela da spec:
  - `Gestor_SemGestorNoEntra_NuloSemAviso`
  - `Gestor_ContaAtivaDoTenant_Resolvido`
  - `Gestor_OidSemContaNoTenant_NuloComAviso`
  - `Gestor_Inativo_NuloComAviso`
  - `Gestor_SiMesmo_NuloComAviso`
  - `Gestor_CicloDeDois_AmbosNulos`
  - `Gestor_CicloDeTres_TodosNulos_ForaDoCicloPreservado` (D → A, com A↔B↔C em ciclo: D fica com o gestor A)
  - `Gestor_JaGravadoQueFicouInativo_ZeraNaProximaRodada`
  - `Gestor_NaoGerido_PodeSerGestor`

  E ainda:
  - `Plan_ContaNaoGerida_NaoEhAlvo`
  - `Plan_SemMudancaEOrigemJaDirectory_SemUpdate`
  - `Plan_PrimeiraVezComTudoNulo_UpdateParaMarcarOrigem`
  - `Plan_CampoComControle_DescartaComAviso_ContaSegue`
  - `Plan_TelefoneEmBranco_ViraNulo` (`BusinessPhone = "  "` cru → update com `null`)
  - `Freio_LimpezaCruaAcimaDoPisoEDaFracao_Dispara` (6 de 10 geridas, piso 5, fração 0,1 → `true`)
  - `Freio_NoPiso_NaoDispara` (5 limpezas → `false`)
  - `Freio_NulosPorResolucaoOuDescarte_NaoContam`
  - `Freio_PreencherEAlterar_NaoContam`
- [ ] **Step 2:** `--filter FullyQualifiedName~DirectoryAttributePlannerTests` → falha.
- [ ] **Step 3:** implementar o planejador.
- [ ] **Step 4:** mesmo filtro → verde.
- [ ] **Step 5:** commit `feat(securegate): planejamento dos atributos do diretorio com regras do gestor e freio`.

---

### Task 5: Etapa no ciclo e log

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Federation/DirectoryRoleReconciler.cs`. Construtor ganha `IDirectoryAttributeStore attributes` (último parâmetro). `DirectorySyncResult` ganha `DirectoryAttributeSyncResult? Attributes = null`.
- Create: `src/SecureGate/Secco.SecureGate.Application/Federation/DirectoryAttributeSyncResult.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Federation/DirectorySyncService.cs` (log)
- Modify: `tests/SecureGate/Secco.SecureGate.Tests/Unit/DirectoryReconcilerRig.cs` (store em memória `InMemoryDirectoryAttributeStore`, passado ao `Build()`)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Unit/DirectoryAttributeSyncTests.cs` e `tests/SecureGate/Secco.SecureGate.Tests/Integration/DirectoryAttributeCycleTests.cs` (no molde de `DirectoryProvisioningCycleTests`, com `factory.Graph`)

**Interfaces:**
- Consumes: Tasks 2, 3 e 4.
- Produces:
  - `public enum DirectoryAttributeSyncOutcome { NotRun, Completed, ConsentRequired, ReadFailed, AbortedByBrake }`
  - `public sealed record DirectoryAttributeSyncResult(DirectoryAttributeSyncOutcome Outcome, int Updated = 0, IReadOnlyList<DirectoryAttributeNotice>? Notices = null, string? ErrorCode = null)`

**Comportamento:** em `ReconcileTenantAsync`, depois do passo 3 (perfis) e só com `provisioningGroupId` não nulo:

1. `GetGroupMemberAttributesAsync`. Falha `Forbidden` → `ConsentRequired`; outra falha → `ReadFailed`.
2. `ListLinkedAsync`, chamado **depois** do provisionamento, para incluir as contas criadas no ciclo.
3. `Plan`, depois `ExceedsBrake` → `AbortedByBrake` sem gravar nada.
4. `ApplyAsync` → `Completed` com `Updated` e `Notices`.

Freio, falha de leitura e retorno antecipado das etapas anteriores → `Attributes` fica `null` (etapa não rodou). Nada desta etapa grava trilha.

**Log** no `DirectorySyncService.ReconcileTenantAsync`, novos `LoggerMessage`:

| EventId | Nível | Mensagem |
| --- | --- | --- |
| 3512 | Warning | `"Atributos do diretório no tenant {TenantId}: sem consentimento User.Read.All; campos inalterados."` |
| 3513 | Warning | `"Atributos do diretório no tenant {TenantId}: leitura falhou ({ErrorCode}); nada mudou."` |
| 3514 | Warning | `"Atributos do diretório no tenant {TenantId}: freio de esvaziamento acionado; nada mudou."` |
| 3515 | Information | `"Atributos do diretório no tenant {TenantId}: {Updated} conta(s) atualizada(s)."` |
| 3516 | Warning | `"Atributos do diretório no tenant {TenantId}: gestor nulo — {NotFound} sem conta, {Inactive} inativo(s), {Self} a própria pessoa, {Cycle} em ciclo; {Discarded} campo(s) descartado(s)."` |
| 3517 | Debug | `"Atributo do diretório: tenant {TenantId}, conta {UserId}, {Reason}."` (um por aviso; `Reason` em kebab-case: `manager-not-found`, `manager-inactive`, `manager-self`, `manager-cycle`, `field-discarded`) |

- 3515: só quando `Updated > 0`.
- 3516: só com avisos.

- [ ] **Step 1: Testes unitários que falham** (`DirectoryAttributeSyncTests`, via rig):
  - `Etapa_SemGrupoDeProvisionamento_NaoRoda` → nenhuma chamada a `GetGroupMemberAttributesAsync`.
  - `Etapa_Forbidden_ConsentRequired_ContasEPerfisJaAplicados` → criação de conta e perfil aconteceram no mesmo ciclo.
  - `Etapa_FalhaDeLeitura_NadaGravado`.
  - `Etapa_FreioDoProvisionamento_EtapaNaoRoda`.
  - `Etapa_FreioDeEsvaziamento_NadaGravado`.
  - `Etapa_NaoGravaTrilha` → `rig.Auditor` sem registro novo da etapa.
  - `Ciclo_ContaCriadaNoCiclo_JaRecebeAtributos`.
- [ ] **Step 2: Teste de integração que falha** (`DirectoryAttributeCycleTests`, banco real com RLS):
  - `Ciclo_GravaAtributos_EReflecteMudancaNaRodadaSeguinte` → muda o cargo no `factory.Graph`, roda `CycleAsync()` de novo e confere.
  - `Ciclo_SemConsentimento_ProvisionamentoSegue` → `factory.Graph` proíbe só atributos; a conta é criada, os campos ficam nulos e a origem `None`.
- [ ] **Step 3:** filtros `~DirectoryAttributeSyncTests|~DirectoryAttributeCycleTests` → falham.
- [ ] **Step 4:** implementar a etapa e o log.
- [ ] **Step 5:** os mesmos filtros + `~DirectoryRoleReconcilerTests|~DirectoryProvisioningTests|~DirectorySyncCycleTests|~DirectoryProvisioningCycleTests` → verdes.
- [ ] **Step 6:** commit `feat(securegate): sincronizacao dos atributos do diretorio no ciclo`.

---

### Task 6: API de diretório, contrato e client

**Files:**
- Modify: `src/SecureGate/Secco.SecureGate.Application/Directory/IDirectoryReader.cs`. `DirectoryPersonData` ganha `string? JobTitle, string? Department, string? BusinessPhone, Guid? ManagerId, DirectoryAttributesOrigin AttributesOrigin`.
- Modify: `src/SecureGate/Secco.SecureGate.Application/Directory/DirectoryPersonDto.cs`. Ganha `string? JobTitle, string? Department, string? BusinessPhone, Guid? ManagerId, string? AttributesSource` e a constante `public const string DirectorySource = "directory";`. Atualizar o doc-comment (deixa de ser "cinco campos").
- Modify: `src/SecureGate/Secco.SecureGate.Infrastructure/Users/DirectoryReader.cs`
- Modify: `src/SecureGate/Secco.SecureGate.Api/openapi/openapi.json` (regenerado)
- Test: `tests/SecureGate/Secco.SecureGate.Tests/Integration/DirectoryApiTests.cs`

**Interfaces:**
- Consumes: colunas da Task 3.
- Produces: no contrato, `jobTitle`, `department`, `businessPhone`, `managerId` e `attributesSource` (string), consumidos pelo `Secco.SecureGate.Client` 0.25.0.

**Visibilidade do gestor:** na projeção, `ManagerId` sai como `user.ManagerId` **só** se `People(tenantId).Any(p => p.Id == user.ManagerId)`; senão `null`. É uma subconsulta, nunca carregar e filtrar em memória.

- [ ] **Step 1: Testes que falham:**
  - `Diretorio_ContaComAtributos_ExpoeCamposEOrigem` → atributos gravados direto no banco por helper; `attributesSource == "directory"`.
  - `Diretorio_ContaLocal_CamposNulosEOrigemNula`.
  - `Diretorio_GestorNaoVisivelNoTenant_ManagerIdNulo` → pessoa do tenant A, membro do B, gestor só em A. Pelo client do B, `managerId` nulo; pelo client do A, preenchido.
  - `Diretorio_JsonDoAttributesSource_EhString` → `JsonValueKind.String`.
  - Ajustar o teste existente que fixa o conjunto de campos para a lista nova, conferindo que **continuam ausentes** segundo fator, logins externos, perfis e motivo de bloqueio.
- [ ] **Step 2:** `--filter FullyQualifiedName~DirectoryApiTests` → falha.
- [ ] **Step 3:** implementar o DTO, os dados e o reader.
- [ ] **Step 4:** regenerar contrato e client:

```bash
SECCO_UPDATE_OPENAPI=true dotnet test tests/SecureGate/Secco.SecureGate.Tests/Secco.SecureGate.Tests.csproj -c Release
dotnet build src/SecureGate/Secco.SecureGate.Client/Secco.SecureGate.Client.csproj -c Release
```

  No diff do `openapi.json`, só podem aparecer as cinco propriedades novas em `DirectoryPersonDto`, com `attributesSource` `type: string`. Nenhum `operationId` muda.
- [ ] **Step 5:** `dotnet build Secco.Platform.slnx -c Release` (o AdminPortal e os outros consumidores do client compilam) e `--filter FullyQualifiedName~DirectoryApiTests` → verdes.
- [ ] **Step 6:** commit `feat(securegate): atributos do diretorio na API directory:read e no client`.

---

### Task 7: Mutação, documentação e verificação final

**Files:**
- Modify: `docs/adr/secco-platform-adrs.md`. Adicionar a **ADR-0059** ao fim: "Atributos de pessoa vindos do diretório federado". Emenda a ADR-0045 (campos do diretório) e a ADR-0055 (o que a sincronização lê e o consentimento `User.Read.All`). Contexto, alternativas 2 e 3 da spec descartadas, decisão, consequências e riscos assumidos da seção Segurança da spec, além do placar da mutação.
- Modify: `src/SecureGate/README.md`, seção de consentimento perto da linha 237. Conteúdo da seção "Consentimento e guia de instalação" da spec: permissão nova, alcance maior que o uso, o que muda para quem já usa e o que confirmar contra tenant real.
- Modify: `CHANGELOG.md`, `docs/roadmap.md`, `docs/design-decisions-log.md` (alternativas descartadas e a escolha de `/manager` por pessoa, com `$expand`/`$batch` como otimização depois da confirmação em tenant real) e o resumo em `CLAUDE.md`, com a ADR-0059 e o client 0.25.0 nas versões correntes **só depois** de publicado.

- [ ] **Step 1: Mutação.** Mutar à mão, rodar os testes da área, ver falhar, reverter e registrar o placar na ADR:
  - (a) `DirectoryReader` devolve `ManagerId` sem a checagem de visibilidade;
  - (b) `ExceedsBrake` sempre `false`;
  - (c) `/manager` com erro não-404 tratado como "sem gestor";
  - (d) `Normalize` trunca em vez de descartar;
  - (e) `Normalize` sem a checagem de controle;
  - (f) gestor inativo resolvido normalmente;
  - (g) ciclo não detectado;
  - (h) `ApplyAsync` sem filtrar conta dona do tenant;
  - (i) etapa roda com o 403, gravando nulos;
  - (j) `Cleared` contando nulos de resolução.

  Todas precisam ser detectadas. Mutante sobrevivente vira teste antes de seguir.
- [ ] **Step 2:** escrever a documentação listada acima.
- [ ] **Step 3: Verificação pelo comando documentado:**

```bash
dotnet build Secco.Platform.slnx --configuration Release
dotnet test Secco.Platform.slnx
```

  Esperado: build sem warning e todos os testes verdes. Falha intermitente de suíte inteira: ver a memória sobre o Ryuk antes de concluir que é regressão.
- [ ] **Step 4:** checklist da skill `secco-platform-standards`: ADRs, camadas, Result, contrato regenerado no mesmo commit da mudança, isolamento de tenant e análise ADR-0020.
- [ ] **Step 5:** commit `docs(securegate): ADR-0059 e guia do consentimento User.Read.All`.
- [ ] **Step 6:** perguntar ao usuário se publica o `Secco.SecureGate.Client` 0.25.0 agora (skill `secco-platform-release`) e avisar que o `secco-intranet` precisa consumir os campos novos e travar a edição quando `attributesSource == "directory"`.
