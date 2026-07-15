# Secco Platform — Perguntas de design e respostas

> **Este arquivo é histórico de processo, não fonte da verdade.** As
> decisões vinculantes estão nas ADRs (`docs/adr/secco-platform-adrs.md`).
> Uma opção não escolhida aqui não está "reprovada" — pode ter sido só
> menor prioridade no momento; reavaliar é legítimo, mas via ADR nova
> se a mudança for adotada.

> Arquivo **não versionado**, gerado a partir do transcript da sessão.
> Cada bloco é uma pergunta feita via AskUserQuestion, com TODAS as opções
> apresentadas (label + descrição) e a resposta escolhida marcada com **✓**.

Total de rodadas de perguntas: 30

---

## Rodada 1

### 1. [Tipo base] Result e Result<T> devem ser classes ou readonly structs?

**Resposta:** Classes (Recomendado)

Opções apresentadas:

- **Classes (Recomendado)** ✓ **ESCOLHIDA**
  - Result<T> : Result permite APIs que tratam ambos uniformemente. Sem o perigo de default(Result<T>) — um struct zerado seria um 'sucesso com valor null', estado inválido silencioso. Custo de alocação é irrelevante no fluxo de request.
- **Readonly structs**
  - Zero alocação, mas sem herança (Result<T> não é Result), e default(Result<T>) cria estado inválido que precisa de guardas extras. FluentResults/CSharpFunctionalExtensions usam classes pelo mesmo motivo.

### 2. [Multi-erro] Como representar múltiplos erros (ex.: validação de comando com 3 campos inválidos)?

**Resposta:** ValidationError agregado (Recomendado)

Opções apresentadas:

- **ValidationError agregado (Recomendado)** ✓ **ESCOLHIDA**
  - Result carrega sempre UM Error; validação usa um subtipo ValidationError que agrega Error[]. API principal fica simples, SDK detecta o subtipo e expande no ProblemDetails (campo errors). Extensível sem breaking change.
- **Result carrega Error[]**
  - Todo Result tem lista de erros (estilo FluentResults). Mais uniforme, porém 99% dos failures têm 1 erro — toda a API paga a complexidade da coleção (qual é 'o' erro para mapear status HTTP?).
- **Só erro único por ora**
  - Adiar multi-erro até a validação existir de fato (FluentValidation entra na Application, Fase 4). Risco: adicionar depois pode forçar breaking change se o shape escolhido divergir.

### 3. [API funcional] Qual superfície funcional o Result deve ter já na v0.1?

**Resposta:** Match + Map + Bind (Recomendado)

Opções apresentadas:

- **Match + Map + Bind (Recomendado)** ✓ **ESCOLHIDA**
  - O trio essencial: Match (consumir na borda — é o que ToHttpResult() do SDK usará), Map (transformar valor), Bind (encadear operações que retornam Result). Adicionar métodos depois é barato; remover é breaking. Inclui variantes async (BindAsync/MapAsync) já que handlers são async.
- **Apenas Match**
  - Superfície mínima: só o consumo na borda. Encadeamento fica em if (result.IsFailure) return ... imperativo, como no exemplo da skill. Menos a aprender; Map/Bind entram quando houver demanda real.
- **Railway completo**
  - Match/Map/Bind/Tap/TapError/Ensure/Combine + async de tudo. Máxima expressividade, mas superfície grande para estabilizar na v0.1 (ADR-0003 exige interface estável) e curva de aprendizado maior para quem entrar no time.

### 4. [Implícitas] Ter conversões implícitas T → Result<T> e Error → Result<T>?

**Resposta:** Ambas (Recomendado)

Opções apresentadas:

- **Ambas (Recomendado)** ✓ **ESCOLHIDA**
  - Handlers ficam limpos: 'return dto;' e 'return PlatformErrors.Tenant.NotResolved;' compilam direto. Padrão consolidado (CSharpFunctionalExtensions, ErrorOr). Caso T seja Error, a ambiguidade é teórica — nunca retornamos Error como valor de sucesso.
- **Só T → Result<T>**
  - Sucesso implícito, falha sempre explícita via Result.Failure<T>(err). Argumento: falha explícita chama atenção no code review; sucesso é o caminho comum e merece a conveniência.
- **Nenhuma**
  - Tudo explícito via factories. Máxima clareza, mais verboso. É o estilo do exemplo atual da skill (Result.Success(dto)).

---

## Rodada 2

### 5. [ApiResponse] O que fazer com o ApiResponse<T> nesta fase?

**Resposta:** Adiar para a Fase 3 (Recomendado)

Opções apresentadas:

- **Adiar para a Fase 3 (Recomendado)** ✓ **ESCOLHIDA**
  - Há uma tensão: a skill manda erros saírem como ProblemDetails (RFC 9457), e os clients NSwag já tipam sucesso/falha — um envelope { success, data, error } duplicaria isso. Pela ADR-0003, tipo sem 2+ consumidores reais e sem shape estável não entra no kernel. O shape correto vai emergir quando o ToHttpResult() do SDK e o primeiro client existirem. Roadmap ganha nota explicando o adiamento.
- **Envelope completo agora**
  - Toda resposta embrulhada em { isSuccess, data, error, correlationId }. Uniformiza consumo, mas conflita com ProblemDetails (erro 4xx viajaria em dois formatos) e fixa um shape de wire antes de existir consumidor — arriscado para um tipo que a ADR-0003 exige estável.
- **Envelope leve só de sucesso**
  - ApiResponse<T> = { data, correlationId } apenas em respostas 2xx; erros seguem ProblemDetails puro. Sem duplicação de formato de erro, mas ainda fixa wire shape sem consumidor e quebra a simetria com PagedResult<T> (que já é seu próprio envelope).

### 6. [PageRequest] O PageRequest deve carregar ordenação/busca ou só paginação?

**Resposta:** Só Page + Size (Recomendado)

Opções apresentadas:

- **Só Page + Size (Recomendado)** ✓ **ESCOLHIDA**
  - Kernel mínimo e estável: paginação é universal; ordenação e busca variam por recurso (campos permitidos, direção, full-text) e são política do produto. Cada API declara seus próprios parâmetros de sort/search por endpoint. Adicionar depois é aditivo (não-breaking); remover seria breaking.
- **Incluir SortBy + SortDirection**
  - PageRequest ganha SortBy (string) e SortDirection (enum Asc/Desc). Conveniente, mas o kernel passa a carregar um contrato de ordenação que cada produto valida de um jeito (whitelist de campos), e string livre de sort no tipo compartilhado convida a inconsistência.
- **Incluir também Search**
  - Page, Size, SortBy, Direction e Search (termo livre). Máxima conveniência para CRUDs, porém semântica de busca é a mais variável de todas (quais campos? like? full-text?) — quase certamente instável demais para a ADR-0003.

---

## Rodada 3

### 7. [ID] Qual a estratégia de ID do BaseEntity?

**Resposta:** Guid v7 no construtor (Recomendado)

Opções apresentadas:

- **Guid v7 no construtor (Recomendado)** ✓ **ESCOLHIDA**
  - BaseEntity fixa Id como Guid gerado com Guid.CreateVersion7() (BCL pura): sequencial no tempo, não fragmenta índice clusterizado no SQL Server (ADR-0018), não exige coordenação entre bancos de tenant (ADR-0005) e permite gerar o ID antes do INSERT. Simples e uniforme em toda a plataforma.
- **BaseEntity<TId> genérico**
  - Cada produto escolhe o tipo do ID (Guid, int, long, strongly-typed ID). Máxima flexibilidade, porém mais cerimônia em todo uso (BaseEntity<Guid> por toda parte), e a plataforma perde a uniformidade 'todo ID é Guid v7' que simplifica SDK, clients e migrations.
- **Identity do banco (int/long)**
  - ID gerado pelo banco no INSERT. Familiar, mas o ID só existe após persistir (complica eventos de domínio e criação em lote), vaza contagem de registros e exige cuidado extra em cenários multi-banco (ADR-0005).

### 8. [Eventos] Eventos de domínio entram no BaseEntity agora?

**Resposta:** Sim, agora (Recomendado)

Opções apresentadas:

- **Sim, agora (Recomendado)** ✓ **ESCOLHIDA**
  - IDomainEvent (marcador) + coleção na BaseEntity com Raise/ClearDomainEvents. É BCL pura, a ADR-0002 já prevê 'eventos de domínio' na camada Domain, e adicionar depois na classe base de TODAS as entidades seria mudança invasiva. O despacho (quem publica os eventos) fica fora do kernel — é papel da Infrastructure/SDK.
- **Adiar para quando houver consumidor**
  - Aplica o critério estrito da ADR-0003 (2+ produtos usando): LogStream pode não precisar de eventos na migração. Risco: quando precisar, mexer no BaseEntity já adotado é mudança sensível; e a lista privada não é breaking para quem não usa.

### 9. [Auditoria] Qual o shape do AuditableEntity?

**Resposta:** 4 campos + ISoftDeletable à parte (Recomendado)

Opções apresentadas:

- **4 campos + ISoftDeletable à parte (Recomendado)** ✓ **ESCOLHIDA**
  - AuditableEntity = CreatedAt/CreatedBy/UpdatedAt/UpdatedBy (DateTimeOffset + string? do claim sub). Soft delete vira interface ISoftDeletable separada — nem toda entidade auditável é soft-deletable; quem precisar implementa e o SDK aplica query filter global. Preenchimento via interceptor do EF no SDK (Fase 3), nunca à mão.
- **Tudo no AuditableEntity**
  - Os 4 campos de auditoria + IsDeleted/DeletedAt/DeletedBy direto na classe. Menos tipos, porém toda entidade auditável carrega colunas de soft delete mesmo quando exclusão é física — colunas mortas no schema.
- **Interfaces segregadas**
  - ICreatedAuditable, IUpdatedAuditable, ISoftDeletable — cada entidade compõe o que precisa; AuditableEntity vira só a combinação das duas primeiras. Máxima granularidade, mas mais tipos no kernel e o interceptor do SDK precisa checar N interfaces.

### 10. [Exceções] Quais exceções base entram no kernel?

**Resposta:** Raiz + DomainInvariantException (Recomendado)

Opções apresentadas:

- **Raiz + DomainInvariantException (Recomendado)** ✓ **ESCOLHIDA**
  - SeccoException (abstrata, raiz de tudo que a plataforma lança) + DomainInvariantException para violação de invariante de domínio — que é bug do chamador, não erro de negócio (esses usam Result<T>, ADR-0004). Guardas de entidade lançam a segunda com mensagem clara. Superfície mínima e coerente com o Result pattern.
- **Só a raiz SeccoException**
  - Apenas a exceção abstrata raiz; invariantes de domínio lançam InvalidOperationException/ArgumentException da BCL. Menos um tipo, mas perde-se a distinção 'invariante de domínio violada' em logs e handlers globais.
- **Hierarquia rica**
  - Raiz + NotFoundException, ConflictException, ValidationException etc. Anti-padrão aqui: essas categorias são erros de NEGÓCIO e já viajam via Result<T>/ErrorType (ADR-0004) — a hierarquia convidaria a usar exceção para fluxo.

---

## Rodada 4

### 11. [Mecanismo] Como o correlation id deve ser carregado durante a requisição e exposto ao código do produto?

**Resposta:** ICorrelationContext próprio (Recomendado)

Opções apresentadas:

- **ICorrelationContext próprio (Recomendado)** ✓ **ESCOLHIDA**
  - Interface simples do SDK (AsyncLocal por baixo) só com CorrelationId. Independente do OpenTelemetry: o X-Correlation-Id é um identificador de negocio/suporte ao cliente (aparece em log, em ticket, no header de resposta), enquanto o TraceId do OTel é formato W3C de 32 hex para tracing distribuido - misturar os dois amarra a API publica do SDK a decisões de tracing. AddLogStream() (ADR-0008) le os dois e enriquece o log com ambos.
- **Usar Activity.Current (OTel) direto**
  - Sem tipo novo: correlation id = TraceId da Activity atual (System.Diagnostics.DiagnosticSource). Evita duplicar conceito de 'requisição atual', mas expõe formato W3C (32 hex, ex. 4bf92f3577b34da6a3ce929d0e0e4736) num header pensado para ser lido por humanos/suporte, e acopla o contrato do SDK ao provider de tracing.
- **Ambos: contexto próprio + tag na Activity**
  - ICorrelationContext para o valor de negocio, e o middleware tambem grava Activity.Current?.SetTag("correlation_id", ...) para o id aparecer correlacionado no trace do OTel. Mais completo, porem mais superficie para estabilizar na v0.1 do SDK.

### 12. [Confiança] Um X-Correlation-Id vindo de fora (cliente externo, outro serviço) deve ser aceito?

**Resposta:** Aceitar só se formato válido, senão gerar (Recomendado)

Opções apresentadas:

- **Aceitar só se formato válido, senão gerar (Recomendado)** ✓ **ESCOLHIDA**
  - ADR-0020: nunca propagar input externo sem validar formato/tamanho. Se o header existir e for um Guid valido (ou o formato escolhido na proxima pergunta), reaproveita - preserva correlacao ponta-a-ponta vinda de um API Gateway ou de outro produto Secco. Se ausente ou invalido (forjado, string gigante, cheio de caracteres de controle para log forging), gera um novo silenciosamente. Nunca lança - so decide gerar ou nao.
- **Sempre gerar no servidor**
  - Ignora qualquer X-Correlation-Id de entrada; o SDK sempre gera um novo por requisição. Elimina de vez o vetor de log forging/spoofing (ADR-0020), mas quebra correlação ponta-a-ponta atraves de um API Gateway ou de chamadas entre produtos Secco que já geram o header.
- **Aceitar qualquer valor não vazio (sanitizado)**
  - Aceita a string recebida desde que nao vazia e dentro de um tamanho maximo, removendo caracteres de controle (defesa contra log forging). Maxima flexibilidade com gateways que usam formatos proprios (nao-Guid), porem abre opçao de qualquer client injetar valores arbitrarios nos logs (ainda que sanitizados).

### 13. [Formato] Qual o tipo/formato do correlation id?

**Resposta:** Guid v7 (Recomendado)

Opções apresentadas:

- **Guid v7 (Recomendado)** ✓ **ESCOLHIDA**
  - Mesmo padrão do Guid.CreateVersion7() já usado em BaseEntity - consistencia na plataforma, ordenavel no tempo (util para debugging cronologico em logs), parse trivial e validação de formato de entrada vira so Guid.TryParse. ICorrelationContext.CorrelationId fica string (representação em texto no header/log), mas a geração interna usa Guid.
- **String opaca livre**
  - Qualquer string e aceita/gerada (ex.: prefixo + timestamp + random, ou apenas Guid v4). Maxima flexibilidade para interoperar com gateways que usam formatos proprios, mas perde a validação de formato simples do Guid.TryParse e a consistencia com o padrao de ID ja adotado no kernel.

### 14. [Escopo] Qual o escopo deste incremento de AddSeccoCorrelation()?

**Resposta:** Middleware + contexto + header de resposta (Recomendado)

Opções apresentadas:

- **Middleware + contexto + header de resposta (Recomendado)** ✓ **ESCOLHIDA**
  - Fecha o ciclo dentro de uma unica API: le/gera o id, populate o ICorrelationContext, devolve o mesmo id no header de resposta. Propagação para chamadas SAINTES (DelegatingHandler nos clients NSwag) fica para quando o primeiro Secco.<Produto>.Client existir (Fase 4) - hoje nao ha client para injetar o handler, e enriquecimento de log fica para AddLogStream() (ADR-0008), tarefa separada.
- **Incluir também DelegatingHandler outbound**
  - Alem do middleware, ja entrega um DelegatingHandler generico que qualquer HttpClient registrado via DI (incluindo os futuros clients NSwag) pode usar para propagar o X-Correlation-Id automaticamente. Mais completo agora, mas adianta uma peça que so tem consumidor real na Fase 4 (ADR-0006) - risco de deixar codigo nao exercitado por testes de integração reais.

---

## Rodada 5

### 15. [Precedência] Quando a requisição traz claim tenant_id E header X-Tenant-Id, qual a regra de precedência e conflito?

**Resposta:** Claim vence; conflito rejeita (Recomendado)

Opções apresentadas:

- **Claim vence; conflito rejeita (Recomendado)** ✓ **ESCOLHIDA**
  - Claim é a fonte primária (ADR-0005): vem assinada pelo SecureGate, é inviolável. Header só é considerado quando NÃO há claim (ex.: chamada serviço-a-serviço com client credentials sem tenant fixo). Se ambos existem e divergem, a requisição é rejeitada (400) — divergência é ou bug ou tentativa de cross-tenant (ADR-0020); nunca escolher silenciosamente.
- **Claim vence; header ignorado**
  - Com claim presente, o header é simplesmente descartado, sem conflito possível. Mais tolerante (um gateway que ecoa headers errados não quebra nada), porém mascara bugs de propagação e silencia um sinal de segurança útil — a divergência nunca é detectada nem logada.
- **Header só em modo interno explícito**
  - Header X-Tenant-Id só é considerado se o produto ativar TenancyOptions.AllowHeaderResolution = true (default false). APIs públicas ficam imunes a spoofing por configuração default; serviços internos ativam conscientemente. Mais seguro por default, porém mais um knob de configuração para acertar.

### 16. [Não resolvido] O que acontece quando nenhuma fonte resolve o tenant?

**Resposta:** Middleware não bloqueia; factory falha (Recomendado)

Opções apresentadas:

- **Middleware não bloqueia; factory falha (Recomendado)** ✓ **ESCOLHIDA**
  - O middleware apenas popula (ou não) o ITenantContext e segue — endpoints públicos, health checks (/health/live não tem tenant!) e o próprio SecureGate funcionam sem tenant. O isolamento é garantido onde importa: ITenantConnectionFactory lança exceção se invocada sem tenant resolvido — acesso a dados sem tenant é impossível por construção, e casos de uso podem retornar Result.Failure(PlatformErrors.Tenant.NotResolved) antes disso.
- **Middleware exige tenant, com rotas opt-out**
  - Requisição sem tenant é rejeitada (401/400) já no middleware, exceto em rotas explicitamente excluídas (/health, /openapi...). Falha mais cedo e mais visível, porém exige manter lista de exceções correta em cada produto — e um esquecimento derruba health check em produção (probe do orquestrador reinicia o serviço em loop).
- **Configurável (strict opcional)**
  - Default não-bloqueante (opção 1) + TenancyOptions.RequireTenant = true para produtos 100% multi-tenant que preferem falhar cedo. Flexível, mas duas semânticas para testar e documentar já na v0.1.

### 17. [Catálogo] De onde vêm as connection strings dos tenants nesta fase (o catálogo do AdminPortal só existe na Fase 7)?

**Resposta:** ITenantCatalog + IConfiguration default (Recomendado)

Opções apresentadas:

- **ITenantCatalog + IConfiguration default (Recomendado)** ✓ **ESCOLHIDA**
  - O SDK define a abstração ITenantCatalog e entrega UMA implementação inicial que lê de IConfiguration (seção Secco:Tenancy:Tenants). Suficiente para LogStream na Fase 4 e para testes; funciona com appsettings, env vars, Azure Key Vault etc. O catálogo SQL gerenciado pelo AdminPortal chega como outra implementação da MESMA interface, sem breaking change. Contrato assíncrono (ValueTask) já preparado para I/O.
- **Já nascer com catálogo em SQL Server**
  - Implementar agora a tabela de catálogo no banco de plataforma (tb_tenants) + cache. Anteciparía a forma final (ADR-0005 fala em catálogo central), porém cria schema de plataforma antes do AdminPortal que o gerencia, sem consumidor real — e adiciona dependência de banco ao SDK na primeira feature de tenancy.
- **Só a interface, sem implementação**
  - SDK define ITenantCatalog e cada produto implementa como quiser. Kernel de contrato mínimo, porém viola o espírito da ADR-0004 (cross-cutting pronto para uso) — todo produto reimplementaria a leitura de configuração, divergindo no formato.

### 18. [Factory] Qual o contrato do ITenantConnectionFactory?

**Resposta:** Connection string (Recomendado)

Opções apresentadas:

- **Connection string (Recomendado)** ✓ **ESCOLHIDA**
  - É literalmente o que a ADR-0005 especifica ('resolve a connection string do tenant'). Agnóstico de provider (ADR-0018: SQL Server E PostgreSQL) — quem cria a conexão/DbContext é a Infrastructure do produto ou o futuro Secco.SDK.EntityFrameworkCore, cada um com seu provider. Superfície mínima para a v0.1.
- **DbConnection aberta**
  - Factory devolve a conexão pronta (DbConnection). Produto nunca vê a connection string (menor risco de vazamento em log), porém o SDK precisa conhecer o provider para instanciar SqlConnection vs NpgsqlConnection — puxa dependência de driver para o SDK base e complica a matriz da ADR-0018.
- **Ambos os métodos**
  - GetConnectionStringAsync + OpenConnectionAsync na mesma interface. Conveniente, mas herda a dependência de drivers do SDK e dobra a superfície a estabilizar; o método de conexão pode entrar depois de forma aditiva quando o Secco.SDK.EntityFrameworkCore existir.

---

## Rodada 6

### 19. [Formato] Qual o formato da resposta dos endpoints de health?

**Resposta:** Live mínimo; ready JSON sem detalhes sensíveis (Recomendado)

Opções apresentadas:

- **Live mínimo; ready JSON sem detalhes sensíveis (Recomendado)** ✓ **ESCOLHIDA**
  - /health/live responde texto puro (Healthy/Unhealthy) — probe de orquestrador só olha status code. /health/ready responde JSON com nome, status e duração de cada check, mas SEM descrições de erro nem exceções (ADR-0020: connection string em mensagem de SqlException vazaria para quem alcançar o endpoint). Diagnóstico detalhado fica nos logs, correlacionado.
- **Ambos mínimos (só status code)**
  - Os dois endpoints respondem texto puro do writer default. Zero superfície de vazamento e zero código de writer, porém operadores perdem o 'qual check falhou?' sem ir aos logs — e o AdminPortal (Fase 7) não teria dado estruturado para exibir.
- **JSON completo com descrições**
  - JSON inclui description e mensagem de exceção de cada check. Máximo diagnóstico direto no endpoint, mas mensagens de exceção frequentemente carregam hostnames, connection strings e paths internos — exatamente o vazamento que a ADR-0020 proíbe em endpoint sem autenticação.

### 20. [Live vs ready] Qual a semântica de live vs ready — quais checks rodam em cada endpoint?

**Resposta:** Live sem checks; ready roda todos (Recomendado)

Opções apresentadas:

- **Live sem checks; ready roda todos (Recomendado)** ✓ **ESCOLHIDA**
  - /health/live com Predicate false: não executa nenhum check — se o processo responde HTTP, está vivo; é o contrato de liveness do Kubernetes (falhou = restart, e reiniciar não conserta um SQL fora do ar). /health/ready executa todos os checks registrados pelo produto — dependencia caída = sai do load balancer, sem restart. Convenção simples, sem tags para errar.
- **Seleção por tags**
  - Checks com tag 'live' rodam no liveness; todos rodam no readiness. Permite liveness custom (ex.: detectar deadlock interno), porém introduz convenção de tag que cada produto precisa conhecer — e um check de dependência externa taggeado errado vira restart-loop em produção.

---

## Rodada 7

### 21. [Pipeline] Usamos o pipeline padrão da Microsoft (AddStandardResilienceHandler) ou definimos um pipeline Secco próprio?

**Resposta:** Standard handler da Microsoft (Recomendado)

Opções apresentadas:

- **Standard handler da Microsoft (Recomendado)** ✓ **ESCOLHIDA**
  - O AddStandardResilienceHandler encadeia rate limiter → timeout total (30s) → retry exponencial com jitter (3 tentativas) → circuit breaker → timeout por tentativa (10s) — valores mantidos e evoluídos pela Microsoft com telemetria integrada. O SDK expõe um delegate para o produto ajustar opções; só sobrescrevemos o que a próxima pergunta decidir (idempotência). Menos código nosso para manter.
- **Pipeline Secco customizado**
  - Montar o pipeline peça a peça (ResiliencePipelineBuilder) com valores escolhidos por nós. Controle total e valores 'da casa', porém viramos mantenedores de decisões que a Microsoft já toma bem (jitter, ordem das estratégias, telemetria) — custo permanente sem ganho claro na v0.1.

### 22. [Escopo] A resiliência é aplicada globalmente a todo HttpClient ou por client (opt-in)?

**Resposta:** Global via defaults (Recomendado)

Opções apresentadas:

- **Global via defaults (Recomendado)** ✓ **ESCOLHIDA**
  - AddSeccoResilience() usa ConfigureHttpClientDefaults: TODO HttpClient registrado via AddHttpClient ganha o pipeline automaticamente — inclusive os futuros clients NSwag da Fase 4, sem código extra. É o espírito da ADR-0004 (comportamento idêntico sem opt-in por produto). Quem precisar de exceção (ex.: client de streaming sem timeout de 30s) ajusta individualmente por cima.
- **Por client, opt-in**
  - Extensão sobre IHttpClientBuilder (AddSeccoResilience() encadeado no AddHttpClient). Controle explícito de onde há retry, porém cada registro esquecido fica sem resiliência — exatamente a variabilidade entre produtos que a ADR-0004 quer eliminar.

### 23. [Idempotência] Retry automático vale para quais métodos HTTP?

**Resposta:** Só métodos idempotentes (Recomendado)

Opções apresentadas:

- **Só métodos idempotentes (Recomendado)** ✓ **ESCOLHIDA**
  - Retry apenas para GET/HEAD/PUT/DELETE/OPTIONS/TRACE. POST/PATCH não têm retry automático: um timeout após o servidor processar duplicaria o efeito (dois lançamentos, duas notificações…). Eles continuam protegidos por timeout e circuit breaker — só não repetem sozinhos. Quando a ADR de idempotência do backlog existir (idempotency keys), POST pode ganhar retry via nova decisão.
- **Retry para todos os métodos**
  - Comportamento default do standard handler: retry em qualquer método quando o erro é transiente. Máxima disponibilidade e zero customização, porém aceita duplicação de efeitos em POST até a plataforma ter idempotency keys — risco real em Notification/Audit.
- **Idempotentes + 429/rede para POST**
  - Meio-termo: POST ganha retry apenas nos casos comprovadamente seguros — 429 (servidor rejeitou sem processar) e falha de conexão antes do request ser enviado. Mais disponível que a opção 1, porém a distinção 'falhou antes de enviar' é sutil de implementar corretamente e difícil de testar — complexidade alta para a v0.1.

---

## Rodada 8

### 24. [Timing] O AddSeccoPlatform() nasce agora com os 4 componentes prontos, ou espera a autenticação (SecureGate, Fase 6)?

**Resposta:** Nasce agora; auth entra aditiva (Recomendado)

Opções apresentadas:

- **Nasce agora; auth entra aditiva (Recomendado)** ✓ **ESCOLHIDA**
  - Agrega correlation + tenancy + health + resilience já — o LogStream (Fase 4) e o template (Fase 5) precisam da composição pronta. AddSeccoAuthentication() entra no agregado quando o SecureGate existir: mudança aditiva, não-breaking (quem chama AddSeccoPlatform() ganha auth de graça na atualização do pacote, que é exatamente a promessa da ADR-0004).
- **Esperar a autenticação**
  - Só criar o agregado quando ele puder ser completo (ADR-0004 lista auth no AddSeccoPlatform). Evita 'composição parcial', porém LogStream e template fariam 4 chamadas individuais que depois precisariam ser trocadas pela composta — churn desnecessário em 2 fases.

### 25. [Pipeline] Criamos também a composição de pipeline (UseSeccoPlatform/MapSeccoPlatform) fixando a ordem dos middlewares?

**Resposta:** Use + Map compostos (Recomendado)

Opções apresentadas:

- **Use + Map compostos (Recomendado)** ✓ **ESCOLHIDA**
  - UseSeccoPlatform() fixa a ordem correta (correlation → [auth futura] → tenancy) — ordem de middleware é exatamente o erro sutil que cada produto cometeria sozinho (tenancy antes de auth = claim invisível). MapSeccoPlatform() mapeia os health checks. Dois pontos porque middlewares e endpoints são estágios diferentes do ASP.NET Core; funciona em qualquer hosting model e continua testável via TestServer.
- **Só o Add; Use* individuais documentados**
  - Produtos chamam UseSeccoCorrelation()/UseSeccoTenancy()/MapSeccoHealthChecks() na ordem que o README manda. Máxima transparência do pipeline, porém a ordem vira disciplina manual em N produtos — e errar a posição do tenancy em relação à auth é silêncio, não erro de build.

### 26. [Opções] O AddSeccoPlatform() terá opções/toggles por componente na v0.1?

**Resposta:** Sem toggles; guarda de chamada dupla (Recomendado)

Opções apresentadas:

- **Sem toggles; guarda de chamada dupla (Recomendado)** ✓ **ESCOLHIDA**
  - AddSeccoPlatform() sem parâmetros: os 4 componentes sempre entram — é a identidade única da ADR-0004 ('nenhum produto implementa cross-cutting localmente'). Ajuste fino continua disponível pelas extensões individuais (ex.: AddSeccoResilience(o => ...) antes do AddSeccoPlatform, que usa TryAdd/guarda para não duplicar). Um marker interno torna chamadas repetidas no-op — chamar duas vezes não duplica pipeline de resiliência.
- **Options com toggles**
  - AddSeccoPlatform(o => { o.Tenancy = false; ... }) permite desligar componentes. Flexibilidade para produtos single-tenant hipotéticos, porém cada toggle é uma combinação a testar e uma brecha para produtos divergirem do padrão — contra o espírito da ADR-0004 na v0.1.

---

## Rodada 9

### 27. [Pluralização] De onde vem o plural do nome da tabela (tb_log_entries)?

**Resposta:** Do nome do DbSet (Recomendado)

Opções apresentadas:

- **Do nome do DbSet (Recomendado)** ✓ **ESCOLHIDA**
  - O EF já usa o nome da propriedade DbSet como nome default da tabela — e o dev JÁ escreve esse plural (DbSet<LogEntry> LogEntries). A convention só faz tb_ + snake_case do nome default: zero regras de inglês para errar (Category→Categories, Status→Statuses...), zero dependência nova, e plural irregular é corrigido renomeando a propriedade. Singular (para id_pk_/id_fk_) vem do nome da classe, que já é singular.
- **Pluralizador próprio no SharedKernel**
  - Implementar regras de plural inglês (BCL pura) no kernel, como o comentário da skill sugere. Funciona sem DbSet, porém regras de inglês têm cauda longa de exceções (person→people, criteria...) — e um plural errado gerado silenciosamente vira nome de tabela em produção.
- **Humanizer como dependência**
  - Pluralização madura e testada pela comunidade. Porém adiciona dependência externa ao SDK.EntityFrameworkCore só para plural de nome de tabela — e Humanizer é um pacote grande para essa função única.

### 28. [Constraints] A convention também nomeia constraints e índices (pk_, fk_, uk_, idx_) automaticamente?

**Resposta:** Sim, tudo automático (Recomendado)

Opções apresentadas:

- **Sim, tudo automático (Recomendado)** ✓ **ESCOLHIDA**
  - A regra de ouro da skill é 'ninguém digita nomes' — vale dobrado para constraints, onde o default do EF (PK_tb_log_entries, FK_tb_..._tb_..._id) viola a notação. A convention renomeia PK (pk_<tabela sem tb_>), FKs (fk_<tabela>_<referenciada>), uniques (uk_<tabela>_<colunas>) e índices (idx_<tabela>_<colunas>) no model finalizing; migrations já saem corretas. O checklist manual da skill vira só conferência.
- **Só tabelas/colunas; constraints manuais**
  - Convention cobre tabelas e colunas; nomes de constraint/índice são ajustados à mão em cada migration, como o checklist da skill descreve hoje. Menos código na convention, porém re-introduz digitação manual repetitiva e sujeita a esquecimento em cada migration de cada produto.

### 29. [Adoção] Como os produtos adotam a convention?

**Resposta:** SeccoDbContext base + convention pública (Recomendado)

Opções apresentadas:

- **SeccoDbContext base + convention pública (Recomendado)** ✓ **ESCOLHIDA**
  - Classe abstrata SeccoDbContext que registra a convention no ConfigureConventions — caminho único e sem esquecimento para os produtos (é o registro que a skill mostra). A SeccoNamingConvention fica pública para o caso raro de um adotante externo que não possa herdar (DbContext de outra base). O futuro interceptor de auditoria (AuditableEntity) e a orquestração de seeding (ADR-0019) têm onde morar.
- **Só a convention + registro documentado**
  - Sem classe base: cada produto sobrescreve ConfigureConventions e adiciona a convention (2 linhas documentadas no README). Menos acoplamento a herança, porém o registro vira disciplina manual — e esquecer não dá erro: o schema simplesmente sai fora do padrão, silenciosamente.

---

## Rodada 10

### 30. [Pacote] Onde moram os contratos e a orquestração de seeding?

**Resposta:** Secco.SDK.EntityFrameworkCore (Recomendado)

Opções apresentadas:

- **Secco.SDK.EntityFrameworkCore (Recomendado)** ✓ **ESCOLHIDA**
  - Seeding é preocupação de dados: os seeders dos produtos vivem em Infrastructure/Seeding (ADR-0019), que já referencia este pacote pelo SeccoDbContext. E o seed de referência também roda FORA de uma API web — no pipeline de provisionamento de tenant e em jobs de migration (ADR-0005: processo controlado) — onde o pacote AspNetCore nem faz sentido. Custo: referência leve a Hosting.Abstractions (IHostEnvironment para a guarda dupla).
- **Secco.SDK.AspNetCore**
  - Tudo do SDK num pacote só. Supõe host web — mas o provisionamento de tenant e jobs de migration são console/worker, que passariam a carregar o framework ASP.NET Core inteiro só pelos contratos de seed.
- **Contratos no SharedKernel**
  - IReferenceDataSeeder/IDevelopmentDataSeeder são interfaces puras — caberiam no kernel. Porém a orquestração (guarda dupla, DI, logging) não cabe (ADR-0003: sem I/O), então o tipo ficaria órfão da sua mecânica; e só Infrastructure implementa seeders — kernel não ganha nada.

### 31. [Disparo] Como a orquestração é disparada?

**Resposta:** Chamada explícita (Recomendado)

Opções apresentadas:

- **Chamada explícita (Recomendado)** ✓ **ESCOLHIDA**
  - await app.Services.SeedSeccoDataAsync() — o chamador decide QUANDO: Program.cs em DEV, pipeline de provisionamento por tenant, job pós-migration. É o 'processo controlado' da ADR-0005 (nada de efeito automático no startup de produção) e é trivial de testar. A guarda dupla continua DENTRO do método: mesmo chamado em produção, o seed de desenvolvimento não executa.
- **IHostedService automático**
  - AddSeccoSeeding() registra um hosted service que roda no startup. Zero código no Program.cs, porém seed vira efeito colateral de subir o processo — em produção com N réplicas, N execuções concorrentes do seed de referência no boot (idempotente, mas corrida no provisionamento), e contraria o espírito de processo controlado da ADR-0005.
- **Ambos**
  - Método explícito + opt-in de hosted service para conveniência em DEV. Duas semânticas para documentar e testar já na v0.1; o hosted service pode entrar depois de forma aditiva se a conveniência fizer falta real no template.

### 32. [Bogus] O Bogus (dados fake pt_BR, ADR-0019) vira dependência do SDK?

**Resposta:** Não — fica no produto (Recomendado)

Opções apresentadas:

- **Não — fica no produto (Recomendado)** ✓ **ESCOLHIDA**
  - O SDK entrega contratos + orquestração; o Bogus é detalhe de implementação do DevelopmentDataSeeder de cada produto (que o referencia na Infrastructure — versão já fixada no Directory.Packages.props). Seeders de REFERÊNCIA nunca usam dados fake, então o SDK carregaria a dependência para todos por causa de metade dos casos. A convenção pt_BR + seed fixo vai documentada no README e materializada no template (Fase 5).
- **Sim, com helper no SDK**
  - SDK referencia Bogus e oferece base SeccoFakerFactory (locale pt_BR + seed fixo prontos). Garante a convenção por construção, porém todo adotante carrega Bogus em produção (pacote de runtime), mesmo quem só tem seed de referência — dependência nova avaliada pela ADR-0020 sem necessidade real.

---

## Rodada 11

### 33. [Client] Quando nasce o projeto Secco.LogStream.Client (NSwag)?

**Resposta:** Na 4.3, com o 1º endpoint real (Recomendado)

Opções apresentadas:

- **Na 4.3, com o 1º endpoint real (Recomendado)** ✓ **ESCOLHIDA**
  - A 4.1 deixa o pipeline pronto — openapi.json gerado no build (nativo .NET 10) e versionado, com validação de drift no CI — mas o projeto Client só nasce quando houver contrato de verdade (POST /logs, 4.3). Um client gerado de um contrato vazio seria pacote sem conteúdo; e o snapshot já versionado garante que a chegada do 1º endpoint apareça como diff revisável.
- **Já na 4.1**
  - Projeto Client existe desde a fundação, gerado do contrato mínimo. Estrutura completa desde o início, porém publica um pacote NuGet sem nenhuma operação útil e adiciona manutenção de pipeline antes de existir consumidor.

### 34. [Migrations] Como as migrations são aplicadas em desenvolvimento?

**Resposta:** Startup em DEV; manual fora (Recomendado)

Opções apresentadas:

- **Startup em DEV; manual fora (Recomendado)** ✓ **ESCOLHIDA**
  - Em Development, o startup itera os tenants do catálogo e aplica migrations em cada banco automaticamente (guarda IsDevelopment) — dev sobe naveg ável com um F5, como o RS.Logging fazia via compose. Fora de DEV, nada automático: migrations via processo controlado (ADR-0005) — o comando/job de provisionamento chega com a 4.6/Fase 7.
- **Sempre manual (script/comando)**
  - Nenhuma migration automática, nem em DEV: script dotnet ef documentado por tenant. Máxima previsibilidade, porém onboarding de dev vira ritual manual por banco — atrito que o seed da ADR-0019 tenta justamente eliminar.

### 35. [Testes 4.1] Os testes de integração da fundação já usam SQL Server real (Testcontainers)?

**Resposta:** Sim, Testcontainers desde já (Recomendado)

Opções apresentadas:

- **Sim, Testcontainers desde já (Recomendado)** ✓ **ESCOLHIDA**
  - ADR-0012: integração com Testcontainers.MsSql é o padrão. A fundação é exatamente onde vale provar o wiring difícil: catálogo → connection string do tenant → DbContext conectando e migrando um SQL Server real. O RS.Logging registrou esse débito (InMemory não exercitava o provider) — não vamos repeti-lo. Requer Docker local e no CI (ubuntu-latest já tem).
- **Só smoke sem banco na 4.1**
  - WebApplicationFactory com health checks e OpenAPI apenas; Testcontainers entra na 4.3 com a primeira entidade. Fundação mais rápida, porém o wiring tenant→banco (a parte mais nova e arriscada) fica sem prova real por duas fases.

---

## Rodada 12

### 36. [Modo DEV] Como a autenticação funciona em DEV/Staging enquanto o SecureGate (Fase 6) não existe?

**Resposta:** HS256 local com trava de produção (Recomendado)

Opções apresentadas:

- **HS256 local com trava de produção (Recomendado)** ✓ **ESCOLHIDA**
  - Espelha o modelo já validado no RS.Logging: fora de Production, uma chave simétrica (Secco:Authentication:DevelopmentSigningKey, mín. 32 chars) permite gerar tokens de teste localmente (jwt.io/script). Fail-fast triplo (ADR-0020): chave definida em Production → API não sobe; nem Authority nem chave → não sobe; ambos definidos → não sobe (ambiguidade é erro). Quando o SecureGate existir, troca-se a chave pela Authority sem mudar código.
- **Sempre OIDC (authority local em DEV)**
  - Um único modo em todos os ambientes: DEV roda um OIDC provider em container (Keycloak/similar) até o SecureGate existir. Máxima fidelidade prod/dev, porém adiciona um container e configuração de realm ao onboarding de TODO dev agora, para ser descartado na Fase 6.
- **dotnet user-jwts**
  - Ferramenta nativa do ASP.NET para tokens de desenvolvimento (dotnet user-jwts create). Zero código de chave simétrica próprio, porém acopla à convenção de configuração Authentication:Schemes:Bearer do ASP.NET (paralela à nossa seção Secco:*), usa user-secrets por dev e não cobre Staging.

### 37. [Agregado] AddSeccoAuthentication() entra no agregado AddSeccoPlatform()/UseSeccoPlatform() já nesta fase?

**Resposta:** Sim, entra já (Recomendado)

Opções apresentadas:

- **Sim, entra já (Recomendado)** ✓ **ESCOLHIDA**
  - ADR-0007: 'todas as APIs validam JWT via AddSeccoAuthentication()' — auth não é opcional na plataforma (ADR-0020). AddSeccoPlatform() passa a exigir a seção Secco:Authentication (fail-fast com mensagem clara se ausente) e UseSeccoPlatform() ganha UseAuthentication/UseAuthorization exatamente entre correlation e tenancy — a ordem que a claim tenant_id exige. É a mudança aditiva prevista quando criamos o agregado.
- **Extensão separada até a Fase 6**
  - AddSeccoAuthentication()/UseSeccoAuthentication() existem mas cada produto chama explicitamente; o agregado só incorpora quando o SecureGate existir. Adia o breaking do agregado, porém deixa dois jeitos de compor a plataforma e a ordem auth→tenancy volta a ser responsabilidade de cada produto.

### 38. [Fallback] Qual a postura de autorização default dos endpoints?

**Resposta:** FallbackPolicy global fail-closed (Recomendado)

Opções apresentadas:

- **FallbackPolicy global fail-closed (Recomendado)** ✓ **ESCOLHIDA**
  - AddSeccoAuthentication() define FallbackPolicy = usuário autenticado: TODO endpoint sem metadata explícita exige token — endpoint novo esquecido nasce protegido (ADR-0020 fail-closed). Exceções são explícitas e auditáveis: MapSeccoHealthChecks() marca AllowAnonymous (probes não autenticam) e o produto decide o OpenAPI. Policies nomeadas/permissões granulares chegam com AddSeccoAuthorization() (ADR-0021, Fase 6).
- **RequireAuthorization por grupo**
  - Cada grupo de endpoints opta por proteção explicitamente (como o RS.Logging fazia). Mais visível no código de cada rota, porém um MapGroup esquecido nasce ABERTO — exatamente o modo de falha que a ADR-0020 manda eliminar.

---

## Rodada 13

### 39. [Fila cheia] O que acontece quando a fila de ingestão (bounded) está cheia?

**Resposta:** Rejeitar com 503 + Retry-After (Recomendado)

Opções apresentadas:

- **Rejeitar com 503 + Retry-After (Recomendado)** ✓ **ESCOLHIDA**
  - Fila cheia → POST responde 503 com Retry-After: o chamador SABE que o log não foi aceito e decide (retry, buffer local, descarte consciente). O futuro AddLogStream() do SDK (ADR-0008) já terá fila local + retry no lado do cliente, então o backpressure é absorvido naturalmente. Perda silenciosa de log é o pior modo de falha de um produto de observabilidade — e fila unbounded (como no RS.Logging) é vetor de OOM/DoS (ADR-0020). Capacidade configurável (default 10.000).
- **Descartar o mais antigo (DropOldest)**
  - A fila nunca rejeita: quando cheia, o log mais antigo ainda não gravado é descartado (com contador logado). API sempre responde 202, porém o 202 vira mentira — o chamador acredita que gravou e o registro pode sumir silenciosamente.
- **Descartar o novo (DropWrite)**
  - Quando cheia, o item novo é descartado silenciosamente e a API responde 202. Mais simples, mesmo problema de honestidade do 202 — e descarta exatamente os logs do momento de pico, que costumam ser os mais valiosos (é quando algo está errado).

### 40. [ID] Qual a estratégia de ID da LogEntry (tabela de altov olume)?

**Resposta:** BaseEntity Guid v7 (Recomendado)

Opções apresentadas:

- **BaseEntity Guid v7 (Recomendado)** ✓ **ESCOLHIDA**
  - Consistência total com a plataforma: id_pk_log_entry Guid v7 (ordenado no tempo, não fragmenta índice), igualdade/eventos herdados, e o ID nasce no servidor ANTES do INSERT — peça-chave da ingestão 100% assíncrona (o 202 pode devolver o id futuro). Custo: 16 bytes/PK vs 8 do bigint — em milhões de linhas são dezenas de MB a mais de índice, irrelevante perto do peso de ds_message/ds_stack_trace na mesma linha.
- **long identity (otimização)**
  - PK bigint 8 bytes como o RS.Logging: índice menor e joins mais baratos. Porém quebra o padrão BaseEntity da plataforma logo no produto de referência (que vira template na Fase 5), reintroduz dependência do banco para conhecer o ID e complica a ingestão assíncrona — o 202 não teria id para devolver.

### 41. [Limites] Quais os limites default de ingestão (ADR-0020 — tudo configurável via options)?

**Resposta:** Perfil balanceado (Recomendado)

Opções apresentadas:

- **Perfil balanceado (Recomendado)** ✓ **ESCOLHIDA**
  - Message até 16 KB (mensagens de log reais raramente passam de 2 KB; 16 KB acomoda payloads serializados), StackTrace até 128 KB (stacks profundas com inner exceptions), batch até 500 itens, body total limitado pelo RequestSizeLimit correspondente. Acima do limite → 400 ProblemDetails com o campo excedido. Valores em SeccoLogStreamOptions, ajustáveis por adotante.
- **Perfil conservador**
  - Message 4 KB, StackTrace 32 KB, batch 100. Minimiza memória/armazenamento e a superfície de DoS, porém stacks .NET reais com inner exceptions estouram 32 KB com frequência — truncamento viraria rotina e perderia informação de diagnóstico.
- **Perfil generoso**
  - Message 64 KB, StackTrace 512 KB, batch 2000. Nunca trunca nada, porém um único batch pode chegar a ~1 GB de payload teórico — exige mais memória na fila bounded e amplia o vetor de DoS que o limite existe para conter.

---

## Rodada 14

### 42. [Rotas] Como os details são expostos nas rotas?

**Resposta:** Aninhados no processo (Recomendado)

Opções apresentadas:

- **Aninhados no processo (Recomendado)** ✓ **ESCOLHIDA**
  - REST canônico: o detail só existe no contexto do pai — POST /api/v1/log-processes/{id}/details (+ /batch). A URL já carrega o processId (sem repetir no body), a relação fica evidente no contrato e o GET do processo pode incluir/paginar os details. É a mudança que a ADR-0010 (kebab, plural, recurso) pede.
- **Planos com processId no body**
  - Como o RS.Logging: POST /log-process/detail com logProcessId no payload. Menos URLs, porém a relação fica implícita no body, o contrato é menos autodescritivo e mistura estilos com o restante da API v1.

### 43. [Campos] Quais campos identificam um LogProcess (além do Id)?

**Resposta:** Name + ExternalReference opcional (Recomendado)

Opções apresentadas:

- **Name + ExternalReference opcional (Recomendado)** ✓ **ESCOLHIDA**
  - Name obrigatório (ds_name: 'ImportacaoPedidos', 'FechamentoDiario') — é o que aparece na auditoria. ExternalReference opcional (ds_external_reference) substitui o confuso 'ProcessId uint' do RS: o chamador guarda ali SEU identificador (número do job, id do lote) para correlação reversa, sem semântica imposta. CorrelationId continua vindo do header, como nas log-entries.
- **Só Name**
  - Mínimo: nome obrigatório e nada mais — correlação fica toda no CorrelationId. Mais enxuto, porém quem migra do RS.Logging perde onde guardar o 'ProcessId' de negócio que já usava, sem alternativa clara.
- **Manter ProcessId numérico do RS**
  - Reproduz o uint ProcessId + Name opcional do RS.Logging. Compatível com o legado, porém perpetua um campo de semântica ambígua (uint de quê?) que a reescrita existe para corrigir.

### 44. [Auditoria] A auditoria (status agregado) é endpoint separado ou embutida na listagem de processos?

**Resposta:** Embutida na listagem (Recomendado)

Opções apresentadas:

- **Embutida na listagem (Recomendado)** ✓ **ESCOLHIDA**
  - O status agregado é SEMPRE computado (subquery MAX(ie_level) — barato) e vem em toda listagem/consulta de processos, com filtro ?status=Error opcional. Um conceito, um endpoint: GET /api/v1/log-processes já É a auditoria — sem a dupla /log-process vs /log-process/audit do RS, que era o mesmo dado com e sem status.
- **Endpoint /audit separado**
  - Como o RS.Logging: listagem crua em /log-processes e visão com status em /log-processes/audit. Mantém a listagem simples mais leve (sem subquery), porém duplica o recurso no contrato e a 'listagem crua' raramente é útil sem o status.

---

## Rodada 15

### 45. [Sanitização] Como o servidor trata dados sensíveis em headers/bodies de ApiCallLog (ADR-0020)?

**Resposta:** Blocklist server-side + limites (Recomendado)

Opções apresentadas:

- **Blocklist server-side + limites (Recomendado)** ✓ **ESCOLHIDA**
  - O servidor sanitiza DEFENSIVAMENTE, sem confiar no chamador: headers cujo nome bate na blocklist (Authorization, Proxy-Authorization, Cookie, Set-Cookie, X-Api-Key + configuráveis) têm o valor substituído por [REDACTED] antes de persistir — um chamador descuidado que logue o header de auth de terceiros não grava o segredo no banco. Bodies são truncados no limite (64 KB) com sufixo indicativo. PII em bodies permanece responsabilidade do chamador (documentado) — o servidor não tem como reconhecê-la.
- **Cru, responsabilidade do chamador**
  - Como o RS.Logging: persiste o que chegar, documentando que o chamador não deve enviar segredos. Zero processamento, porém um único chamador descuidado grava tokens de terceiros num banco de logs com retenção longa — exatamente o vazamento que a ADR-0020 manda prevenir por construção.
- **Não aceitar headers**
  - Elimina o vetor removendo o campo: ApiCallLog só grava url/método/status/duração/bodies. Máxima segurança, porém perde diagnóstico legítimo (Content-Type, X-Request-Id, headers de rate limit) útil para depurar integrações — o caso de uso central do recurso.

---

## Rodada 16

### 46. [Postura] A retenção vem ativa por default ou é opt-in explícito?

**Resposta:** Opt-in explícito (Recomendado)

Opções apresentadas:

- **Opt-in explícito (Recomendado)** ✓ **ESCOLHIDA**
  - Sem DefaultDays configurado, o worker não expurga NADA (e loga que está inativo). Apagar dados silenciosamente por default é o tipo de surpresa que não se perdoa num produto de logs — e conformidade/LGPD por produto é ADR futura do backlog: até lá, quem apaga decide conscientemente. O template (Fase 5) nasce com o valor comentado e documentado.
- **Ativa com 30 dias por default**
  - Higiene por construção: quem não configura nada tem 30 dias de retenção (como o RS rodava). Evita bancos crescendo sem limite, porém um adotante desavisado perde logs de 31 dias atrás sem ter decidido isso — deleção de dados como efeito colateral de default.

### 47. [Janelas] A janela de retenção é única ou por tipo de log?

**Resposta:** Única + override por tenant (Recomendado)

Opções apresentadas:

- **Única + override por tenant (Recomendado)** ✓ **ESCOLHIDA**
  - Um DefaultDays para as três tabelas (log-entries, log-processes+details via cascade, api-call-logs) + DaysByTenant para contratos com retenção diferente (como o RS tinha). Um conceito só para operar e documentar; janela por TIPO entra depois de forma aditiva se algum adotante precisar (ex.: api-call-logs com bodies pesados por menos tempo).
- **Por tipo de log**
  - DefaultDays separado para entries/processes/api-calls (api-call-logs com bodies de 64 KB pesam mais). Controle fino desde já, porém 3 knobs × override por tenant = matriz de configuração grande para a v1 sem demanda real.

### 48. [Política] Onde vive a política de retenção por tenant nesta fase?

**Resposta:** Configuração do produto (Recomendado)

Opções apresentadas:

- **Configuração do produto (Recomendado)** ✓ **ESCOLHIDA**
  - Seção LogStream:Retention no appsettings/env vars — como o RS fazia e coerente com o catálogo de tenants por configuração desta fase. Quando o AdminPortal (Fase 7) trouxer o catálogo SQL gerenciado, a política por tenant migra para lá junto (mesma interface ITenantCatalog evolui) — sem breaking no worker.
- **Metadados no catálogo (TenantInfo)**
  - TenantInfo do SDK ganharia um dicionário de metadados onde o LogStream guarda RetentionDays. Anteciparía a forma final, porém polui o contrato do SDK com política de UM produto e força breaking no TenantInfo recém-publicado — sem o AdminPortal que gerenciaria isso.

---

## Rodada 17

### 49. [Full-text] Full-text search: o que fazemos na v1 (o full-text nativo do MariaDB ficou no legado)?

**Resposta:** LIKE na v1; full-text vai ao backlog (Recomendado)

Opções apresentadas:

- **LIKE na v1; full-text vai ao backlog (Recomendado)** ✓ **ESCOLHIDA**
  - A busca por substring (LIKE, já implementada) cobre o caso de uso de depuração típico. Full-text no SQL Server exige FULLTEXT CATALOG/INDEX provisionado POR BANCO DE TENANT (SQL cru por provider, ADR-0018), não funciona em todas as edições e adiciona complexidade operacional real — custo alto sem demanda comprovada. Entra no backlog e só volta com caso real (e talvez junto de uma decisão maior de busca, ex.: Elastic do backlog do RS).
- **CONTAINS no SQL Server agora**
  - Provisionar catálogo/índice full-text via migration com SQL cru e usar CONTAINS quando o parâmetro q= vier. Busca linguística melhor, porém: setup por banco de tenant no provisionamento, indisponível em algumas edições/containers, e o equivalente Postgres (tsvector) teria que nascer junto na mesma fase (ADR-0018: paridade) — dobra o escopo da 4.7.

### 50. [Migrations] Como estruturar as migrations PostgreSQL (ADR-0018: assemblies separados por engine)?

**Resposta:** Dois projetos de migrations (Recomendado)

Opções apresentadas:

- **Dois projetos de migrations (Recomendado)** ✓ **ESCOLHIDA**
  - Conforme a ADR-0018 literal: Secco.LogStream.Migrations.SqlServer e Secco.LogStream.Migrations.Postgres, cada um só com as migrations do seu engine; a Infrastructure seleciona via MigrationsAssembly conforme LogStream:Database:Provider. As migrations SqlServer existentes MOVEM para o projeto novo (histórico de migration preservado — mesmos ids). É o layout que o template (Fase 5) vai destilar.
- **Pastas no mesmo assembly (como o RS)**
  - Migrations/SqlServer e Migrations/Postgres no proprio Infrastructure, com seleção por namespace via IMigrationsAssembly customizado (o hack NamespacedMigrationsAssembly do RS.Logging). Menos projetos, porém contraria a ADR-0018 ('assemblies separados') e depende de serviço interno do EF que quebra entre versões.

### 51. [Matriz PG] Qual o escopo da matriz de testes PostgreSQL?

**Resposta:** Migrations + smoke E2E no PG (Recomendado)

Opções apresentadas:

- **Migrations + smoke E2E no PG (Recomendado)** ✓ **ESCOLHIDA**
  - PostgreSQL ganha: aplicação das migrations num container Testcontainers.PostgreSql + um smoke E2E (ingestão→consulta) provando o produto vivo no segundo provider — incluindo a promessa da ADR-0017 de nomenclatura idêntica (minúsculas sem aspas). A suite completa continua no provider padrão (SQL Server); a matriz plena entra se/quando um adotante PG real aparecer — dobra o tempo de CI hoje sem déficit de cobertura lógica (a lógica é a mesma, só o dialeto muda).
- **Suite completa nos dois providers**
  - Toda a suite de integração parametrizada por provider. Máxima confiança de paridade, porém dobra o tempo de CI (hoje ~1 min de testes LogStream vira ~2+) em toda mudança, para exercitar diferenças que são quase todas de dialeto já cobertas pelo EF.

---

## Rodada 18

### 52. [Exemplo] O template inclui um recurso de negócio de exemplo completo?

**Resposta:** Sim, exemplo completo sempre (Recomendado)

Opções apresentadas:

- **Sim, exemplo completo sempre (Recomendado)** ✓ **ESCOLHIDA**
  - Um recurso 'Sample' de ponta a ponta — entidade BaseEntity, handler Result<T>, endpoint 202/busca paginada, migration pela convention, teste unitário + integração Testcontainers — é a demonstração EXECUTÁVEL de todos os padrões (é o que a ADR-0013 chama de 'testes de exemplo'). O dev novo copia o padrão em vez de inventar; apagar a pasta Sample depois é trivial. Template sem condicionais = manutenção simples.
- **Esqueleto mínimo**
  - Só a fundação: camadas vazias, AddSeccoPlatform, health, OpenAPI, testes de fundação. Nada para apagar, porém o dev fica sem referência executavel do padrão (handler, convention, 202) no proprio produto — e volta a copiar do LogStream à mão, o que o template existe para evitar.
- **Parâmetro --include-sample**
  - Exemplo condicional via template.json (default true). Flexível, porém cada arquivo condicionado é complexidade permanente de manutenção do template — e a ADR-0013 exige o template sincronizado com as ADRs: quanto mais simples, melhor ele cumpre esse papel.

### 53. [Referências] O produto gerado referencia a plataforma como projetos (monorepo) ou pacotes NuGet?

**Resposta:** ProjectReference — monorepo-first (Recomendado)

Opções apresentadas:

- **ProjectReference — monorepo-first (Recomendado)** ✓ **ESCOLHIDA**
  - O consumidor real do template é a Fase 6: o SecureGate nasce DENTRO do monorepo, onde ProjectReference é o padrão (ADR-0001: refatorações no kernel são commits atômicos). O template gera caminhos relativos assumindo src/<Produto>/ no monorepo. A variante para adotante EXTERNO (PackageReference no feed) entra quando houver adotante externo real — parâmetro aditivo futuro.
- **PackageReference (NuGet)**
  - Produto gerado consome Secco.* do GitHub Packages — o modelo do adotante externo independente. Porém o primeiro consumidor (SecureGate, no monorepo) teria que trocar as referências na mão, e o ciclo de evolução kernel/SDK durante as Fases 6-7 ficaria acoplado a publicações de pacote a cada ajuste.

---

## Rodada 19

### 54. [Base OIDC] Qual a base do OIDC provider do SecureGate? (vira ADR nova)

**Resposta:** OpenIddict (Recomendado)

Opções apresentadas:

- **OpenIddict (Recomendado)** ✓ **ESCOLHIDA**
  - O padrão OSS de facto para OIDC server em ASP.NET Core: Apache 2.0 (sem a armadilha de licença do Duende, mesmo padrão FluentAssertions/MassTransit já registrado nas ADRs), manutenção ativa, integração nativa com EF Core e ASP.NET Identity (que a ADR-0021 já adota). Implementar protocolo criptográfico à mão é exatamente o risco que a ADR-0020 manda evitar. Proponho a ADR-0022 formalizando.
- **Duende IdentityServer**
  - Maduro e muito difundido, PORÉM licença comercial paga para uso em produção — a mesma armadilha que as ADRs já rejeitaram duas vezes (FluentAssertions v8, MassTransit v9). Incompatível com a promessa de adotante self-hosted sem custo de licença de terceiros.
- **Implementação própria (JWT manual)**
  - Endpoints de token/JWKS escritos à mão, como o esboço HS256 atual do SDK. Controle total, porém implementar OIDC/OAuth2 corretamente (rotação de chaves, validações de flow, PKCE, replay) é terreno clássico de vulnerabilidade — violação direta do espírito da ADR-0020 para o componente MAIS sensível da plataforma.

### 55. [User store] Onde vivem os usuários e credenciais?

**Resposta:** Banco de plataforma do SecureGate (Recomendado)

Opções apresentadas:

- **Banco de plataforma do SecureGate (Recomendado)** ✓ **ESCOLHIDA**
  - Identidade é DADO DE PLATAFORMA, não dado de negócio do tenant — mesma natureza do catálogo de tenants (ADR-0005 já prevê o catálogo central). Um banco do SecureGate com ASP.NET Identity (usuários com tenant_id, roles POR TENANT conforme ADR-0021, clients OIDC, catálogo). Login resolve o tenant do usuário sem descoberta ambígua; só o SecureGate acessa esse banco (nenhuma query de produto cruza nada). Backup/LGPD de identidade tratados como plataforma.
- **Usuários no banco de cada tenant**
  - Aplica database-per-tenant também à identidade: isolamento físico máximo das credenciais. Porém o fluxo de login precisa descobrir o tenant ANTES de autenticar (email ambíguo entre tenants, seletor de tenant na tela de login), o OIDC provider passa a abrir N conexões, e roles/permissões por tenant (ADR-0021) exigem consultas distribuídas — complexidade alta para a v1.

### 56. [Sequência] Qual fluxo entra primeiro na Fase 6?

**Resposta:** Client credentials primeiro (Recomendado)

Opções apresentadas:

- **Client credentials primeiro (Recomendado)** ✓ **ESCOLHIDA**
  - Máquina-a-máquina destrava o valor imediato: produtos validando JWT contra Authority REAL (JWKS/discovery — aposentando a chave HS256 de dev), tokens serviço-a-serviço com scope (ADR-0007) e o AddSeccoAuthorization (ADR-0021) sobre roles resolvidos. Login de USUÁRIO (authorization code + PKCE, telas) entra na sequência, a tempo do AdminPortal (Fase 7) — que é seu primeiro consumidor real.
- **Tudo junto (users + m2m)**
  - Fase 6 só fecha com login de usuário completo (telas, code flow, PKCE, refresh). Entrega o produto inteiro de uma vez, porém adia por semanas o benefício que já está maduro (Authority real para os produtos) e o primeiro consumidor de login é o AdminPortal, que ainda não existe.

---

## Rodada 20

### 57. [Schema IAM] O schema do Identity/OpenIddict no banco do SecureGate segue a ADR-0017 ou fica como exceção de framework?

**Resposta:** ADR-0017 completa (Recomendado)

Opções apresentadas:

- **ADR-0017 completa (Recomendado)** ✓ **ESCOLHIDA**
  - Tabelas re-nomeadas com ~11 chamadas ToTable após o base.OnModelCreating (tb_users, tb_roles, tb_openiddict_applications...) e colunas DE GRAÇA pela SeccoNamingConvention (Identity/OpenIddict não fixam nomes de coluna). Custo real baixo, schema 100% consistente — qualquer query no banco mais sensível da plataforma revela tipo e papel das colunas (o argumento da ADR-0017 vale dobrado aqui). Risco: numa major do Identity/OpenIddict que adicione tabela nova, ela nasce fora do padrão até o ToTable ser adicionado — detectável por teste de schema.
- **Exceção registrada para tabelas de framework**
  - AspNetUsers/OpenIddictApplications ficam com os nomes de fábrica (como já aceitamos o __EFMigrationsHistory); só as entidades PRÓPRIAS do SecureGate (tb_tenants, tb_permissions) seguem a convention. Zero manutenção em upgrades, porém o banco mais sensível da plataforma fica com dois padrões de nomenclatura misturados — e a exceção precisaria ser registrada em ADR (a 0017 diz 'todo objeto de banco').

---

## Rodada 21

### 58. [Certificados] Como os certificados de assinatura/criptografia do SecureGate são geridos por ambiente?

**Resposta:** Dev automático + prod explícito fail-fast (Recomendado)

Opções apresentadas:

- **Dev automático + prod explícito fail-fast (Recomendado)** ✓ **ESCOLHIDA**
  - Fora de Production: certificados de desenvolvimento do próprio OpenIddict (gerados e persistidos automaticamente — zero atrito; em Testing, chaves efêmeras em memória). Em Production: certificado obrigatório via configuração (path+senha ou thumbprint no store) — sem certificado, a API NÃO SOBE (mesmo padrão fail-fast da autenticação do SDK, ADR-0020). Rotação em produção é operação consciente do adotante, documentada.
- **Chaves persistidas no banco**
  - Chaves geradas e armazenadas em tb_signing_keys com rotação automática. Rotação sem intervenção, porém coloca material criptográfico privado no banco (amplia o impacto de um vazamento de backup) e reimplementa gestão de chaves — exatamente o tipo de código que a ADR-0020/0022 manda não escrever à mão.

### 59. [Scopes] Qual a granularidade dos scopes OAuth por produto?

**Resposta:** Um scope por produto (Recomendado)

Opções apresentadas:

- **Um scope por produto (Recomendado)** ✓ **ESCOLHIDA**
  - Scope = acesso ao produto (logstream → audience secco-logstream, o valor que o AddSeccoAuthentication de cada produto já valida). Autorização FINA não duplica no token: é papel do role→permissions da ADR-0021 (resolvido em runtime, revogável). Scope novo = seed de referência novo quando nasce produto. Simples de operar e raciocinar.
- **Scopes granulares por operação**
  - logstream:read, logstream:write etc. Controle no próprio token, porém duplica o modelo recurso:ação da ADR-0021 com semântica diferente (scope = estático no token, permission = revogável em runtime) — duas fontes de verdade para a mesma pergunta.

---

## Rodada 22

### 60. [Base OIDC] Qual a base do OIDC provider do SecureGate? (vira ADR nova)

**Resposta:** OpenIddict (Recomendado)

Opções apresentadas:

- **OpenIddict (Recomendado)** ✓ **ESCOLHIDA**
  - O padrão OSS de facto para OIDC server em ASP.NET Core: Apache 2.0 (sem a armadilha de licença do Duende, mesmo padrão FluentAssertions/MassTransit já registrado nas ADRs), manutenção ativa, integração nativa com EF Core e ASP.NET Identity (que a ADR-0021 já adota). Implementar protocolo criptográfico à mão é exatamente o risco que a ADR-0020 manda evitar. Proponho a ADR-0022 formalizando.
- **Duende IdentityServer**
  - Maduro e muito difundido, PORÉM licença comercial paga para uso em produção — a mesma armadilha que as ADRs já rejeitaram duas vezes (FluentAssertions v8, MassTransit v9). Incompatível com a promessa de adotante self-hosted sem custo de licença de terceiros.
- **Implementação própria (JWT manual)**
  - Endpoints de token/JWKS escritos à mão, como o esboço HS256 atual do SDK. Controle total, porém implementar OIDC/OAuth2 corretamente (rotação de chaves, validações de flow, PKCE, replay) é terreno clássico de vulnerabilidade — violação direta do espírito da ADR-0020 para o componente MAIS sensível da plataforma.

### 61. [User store] Onde vivem os usuários e credenciais?

**Resposta:** Banco de plataforma do SecureGate (Recomendado)

Opções apresentadas:

- **Banco de plataforma do SecureGate (Recomendado)** ✓ **ESCOLHIDA**
  - Identidade é DADO DE PLATAFORMA, não dado de negócio do tenant — mesma natureza do catálogo de tenants (ADR-0005 já prevê o catálogo central). Um banco do SecureGate com ASP.NET Identity (usuários com tenant_id, roles POR TENANT conforme ADR-0021, clients OIDC, catálogo). Login resolve o tenant do usuário sem descoberta ambígua; só o SecureGate acessa esse banco (nenhuma query de produto cruza nada). Backup/LGPD de identidade tratados como plataforma.
- **Usuários no banco de cada tenant**
  - Aplica database-per-tenant também à identidade: isolamento físico máximo das credenciais. Porém o fluxo de login precisa descobrir o tenant ANTES de autenticar (email ambíguo entre tenants, seletor de tenant na tela de login), o OIDC provider passa a abrir N conexões, e roles/permissões por tenant (ADR-0021) exigem consultas distribuídas — complexidade alta para a v1.

### 62. [Sequência] Qual fluxo entra primeiro na Fase 6?

**Resposta:** Client credentials primeiro (Recomendado)

Opções apresentadas:

- **Client credentials primeiro (Recomendado)** ✓ **ESCOLHIDA**
  - Máquina-a-máquina destrava o valor imediato: produtos validando JWT contra Authority REAL (JWKS/discovery — aposentando a chave HS256 de dev), tokens serviço-a-serviço com scope (ADR-0007) e o AddSeccoAuthorization (ADR-0021) sobre roles resolvidos. Login de USUÁRIO (authorization code + PKCE, telas) entra na sequência, a tempo do AdminPortal (Fase 7) — que é seu primeiro consumidor real.
- **Tudo junto (users + m2m)**
  - Fase 6 só fecha com login de usuário completo (telas, code flow, PKCE, refresh). Entrega o produto inteiro de uma vez, porém adia por semanas o benefício que já está maduro (Authority real para os produtos) e o primeiro consumidor de login é o AdminPortal, que ainda não existe.

---

## Rodada 23

### 63. [Schema IAM] O schema do Identity/OpenIddict no banco do SecureGate segue a ADR-0017 ou fica como exceção de framework?

**Resposta:** ADR-0017 completa (Recomendado)

Opções apresentadas:

- **ADR-0017 completa (Recomendado)** ✓ **ESCOLHIDA**
  - Tabelas re-nomeadas com ~11 chamadas ToTable após o base.OnModelCreating (tb_users, tb_roles, tb_openiddict_applications...) e colunas DE GRAÇA pela SeccoNamingConvention (Identity/OpenIddict não fixam nomes de coluna). Custo real baixo, schema 100% consistente — qualquer query no banco mais sensível da plataforma revela tipo e papel das colunas (o argumento da ADR-0017 vale dobrado aqui). Risco: numa major do Identity/OpenIddict que adicione tabela nova, ela nasce fora do padrão até o ToTable ser adicionado — detectável por teste de schema.
- **Exceção registrada para tabelas de framework**
  - AspNetUsers/OpenIddictApplications ficam com os nomes de fábrica (como já aceitamos o __EFMigrationsHistory); só as entidades PRÓPRIAS do SecureGate (tb_tenants, tb_permissions) seguem a convention. Zero manutenção em upgrades, porém o banco mais sensível da plataforma fica com dois padrões de nomenclatura misturados — e a exceção precisaria ser registrada em ADR (a 0017 diz 'todo objeto de banco').

---

## Rodada 24

### 64. [Modelo] Como modelar as connection strings no catálogo? (database-per-tenant, ADR-0005: cada produto tem seu próprio banco por tenant — ex.: secco_logstream_acme e secco_securegate compartilham o servidor, mas são bancos distintos)

**Resposta:** TenantDatabase por produto (Recomendado)

Opções apresentadas:

- **TenantDatabase por produto (Recomendado)** ✓ **ESCOLHIDA**
  - Entidade TenantDatabase: (tenant, produto) → connection string, única por par. Reflete a realidade do database-per-tenant (o banco do LogStream do tenant X não é o banco de outro produto), permite provisionar produtos gradualmente por tenant e cada produto só consulta as próprias entradas. É a forma que o retention worker do LogStream já espera via ListAsync.
- **Uma connection string por tenant**
  - Campo único no Tenant. Simples, mas força todos os produtos a compartilharem o mesmo banco físico por tenant — contradiz o isolamento por produto já praticado (secco_logstream_* separado) e impede mover produtos de servidor independentemente.
- **Convenção servidor + nome derivado**
  - Catálogo guarda só servidor/credenciais por tenant; o nome do banco é derivado por convenção (secco_<produto>_<slug>). Menos linhas no catálogo, porém rígido: renomear/migrar um banco específico exige quebrar a convenção, e credenciais únicas por servidor ampliam o raio de dano (ADR-0020).

### 65. [Autorização] Como proteger os endpoints do catálogo? Connection strings são segredos — quem pode ler o quê é a decisão de segurança central da fase (ADR-0020).

**Resposta:** Scope catálogo por produto (Recomendado)

Opções apresentadas:

- **Scope catálogo por produto (Recomendado)** ✓ **ESCOLHIDA**
  - Seed de referência ganha scopes catalog:<produto> (ex.: catalog:logstream). O client credentials do serviço LogStream recebe só catalog:logstream e o endpoint GET /catalog/{produto}/... exige o scope correspondente — least privilege: comprometer o client de um produto não vaza connection strings dos demais. Endpoints de gestão (criar tenant/banco) exigem scope securegate:admin (só AdminPortal/operador).
- **Scope único 'securegate'**
  - Qualquer portador do scope securegate lê o catálogo inteiro de qualquer produto. Mais simples de operar, mas um client de produto comprometido expõe as connection strings de toda a plataforma; refinamento ficaria para as permissions da ADR-0021 (6.4).
- **API só com metadados**
  - A API expõe apenas tenants (id, slug, ativo); connection strings continuam em configuração/Key Vault de cada produto. Elimina segredo em trânsito, mas NÃO cumpre o objetivo da fase (substituir o catálogo por configuração fora de DEV) — cadastrar tenant continuaria exigindo redeploy de config.

### 66. [Pacote] Onde vive a implementação remota de ITenantCatalog que os produtos consomem?

**Resposta:** No Secco.SecureGate.Client (Recomendado)

Opções apresentadas:

- **No Secco.SecureGate.Client (Recomendado)** ✓ **ESCOLHIDA**
  - O pacote do client NSwag (ADR-0006) traz junto SecureGateTenantCatalog : ITenantCatalog + AddSecureGateTenantCatalog() que substitui o registro padrão. O SDK permanece agnóstico de produto (referenciar um client de produto no SDK inverteria a direção de dependência); produto que quer o catálogo central adiciona 1 pacote + 1 linha.
- **No SDK (Secco.SDK.AspNetCore)**
  - O SDK referenciaria Secco.SecureGate.Client e ofereceria a implementação embutida. Uma dependência a menos para os produtos, mas acopla o SDK a um produto específico — todo consumidor do SDK carregaria o client do SecureGate mesmo sem usá-lo, e o versionamento dos dois passa a andar amarrado.

### 67. [Resiliência] Cache e comportamento quando o SecureGate está indisponível? (o catálogo entra no caminho de TODA requisição multi-tenant dos produtos)

**Resposta:** TTL curto + stale em falha (Recomendado)

Opções apresentadas:

- **TTL curto + stale em falha (Recomendado)** ✓ **ESCOLHIDA**
  - Cache in-memory por (tenant, produto) com TTL ~5 min; se o SecureGate estiver indisponível na renovação, serve a entrada expirada (stale) e loga warning — SecureGate fora do ar não derruba produtos que já resolveram o tenant. Tenant nunca visto + SecureGate fora = 503 com Retry-After. Nota: diferente da autorização (ADR-0021, fail-closed) — connection string é dado de conectividade, não decisão de acesso; servir stale não concede nada que o tenant já não tinha.
- **Fail-closed estrito**
  - Entrada expirada + SecureGate indisponível = tenant não resolve (503). Postura mais conservadora (revogação de tenant propaga em no máximo 1 TTL mesmo em falha), ao custo de transformar qualquer indisponibilidade do SecureGate em indisponibilidade de TODOS os produtos após o TTL — o SecureGate vira ponto único de falha da plataforma inteira.
- **Sem cache**
  - Toda requisição consulta o catálogo. Consistência imediata, mas adiciona uma chamada HTTP na latência de cada request de todos os produtos e multiplica a carga no SecureGate — não recomendado.

---

## Rodada 25

### 68. [Máquinas] Como tokens de MÁQUINA (client credentials, sem usuário) passam por endpoints protegidos por permissão? O token de serviço do LogStream de hoje só carrega sub+scope — sem role, o fail-closed da ADR-0021 negaria tudo.

**Resposta:** Clients recebem roles (Recomendado)

Opções apresentadas:

- **Clients recebem roles (Recomendado)** ✓ **ESCOLHIDA**
  - O client OIDC ganha roles configuráveis (coluna na entidade OidcApplication própria); o /connect/token emite a claim curta 'role' também para máquinas. Modelo Único de autorização (tudo tem role → permissions por tenant, ADR-0021 intacta), least privilege igual para humanos e serviços, e a resolução (tenant do X-Tenant-Id, role do token) já funciona sem caso especial.
- **Bypass por scope de produto**
  - Portador do scope do produto (ex.: logstream) passa nas checagens de permissão daquele produto — máquina confiável por definição. Simples, mas cria dois modelos de autorização paralelos e um client comprometido tem TODAS as ações do produto (contra o espírito least-privilege da ADR-0020/0021).
- **Permissões só para usuários**
  - Endpoints com RequirePermission ficam inacessíveis a client credentials; máquinas usam apenas endpoints sem permissão granular. Adia o problema — mas a ingesão do LogStream é justamente chamada por máquinas, então os endpoints principais não poderiam ganhar permissões.

### 69. [Scope authz] Quem pode consultar o endpoint role→permissions do SecureGate? (permissões não são segredos como connection strings — são configuração de acesso)

**Resposta:** Scope único authorization:read (Recomendado)

Opções apresentadas:

- **Scope único authorization:read (Recomendado)** ✓ **ESCOLHIDA**
  - Um scope de plataforma no seed de referência; todo serviço que usa AddSeccoAuthorization() o recebe. Vazamento eventual revela nomes de permissões/roles (baixa sensibilidade), não credenciais. Gestão (criar role, conceder/revogar permissão) continua exclusiva do securegate:admin.
- **Por produto (authz:<produto>)**
  - Espelha o catalog:<produto> da 6.3. Mais granular, porém permissões não são particionadas por produto no armazenamento (um role reúne permissões de vários produtos) — filtrar por prefixo de recurso adicionaria complexidade sem proteção real.
- **Qualquer token autenticado**
  - Sem scope dedicado — só a FallbackPolicy. Menos setup, mas qualquer portador de token da plataforma enumera o modelo de acesso de todos os tenants (reconhecimento facilitado, ADR-0020).

### 70. [Constantes] Onde vivem as constantes de permissão? A ADR-0021 exemplifica 'SeccoPermissions no SharedKernel', mas a ADR-0003 só admite no kernel tipos usados por 2+ produtos — e permissões do LogStream (log-entries:read) são do produto.

**Resposta:** Cada produto define as suas (Recomendado)

Opções apresentadas:

- **Cada produto define as suas (Recomendado)** ✓ **ESCOLHIDA**
  - LogStreamPermissions vive no próprio produto (Application); o SharedKernel ganha só o tipo utilitário comum (validação/composição do formato recurso:acao) que TODOS usam. Respeita a regra de admissão da ADR-0003; o exemplo da ADR-0021 é lido como ilustração do padrão, não como localização obrigatória — registrado na entrega.
- **Tudo em SeccoPermissions no kernel**
  - Segue a letra da ADR-0021: catálogo central de constantes de todos os produtos no SharedKernel. Um só lugar para o AdminPortal descobrir permissões, porém o kernel passa a mudar a cada permissão nova de qualquer produto — exatamente o acoplamento que a ADR-0003 proíbe.

### 71. [Adoção] O LogStream adota as permissões já nesta fase? (implica resolver por configuração para DEV/testes — mesmo padrão do catálogo 6.3 — e ajustar os tokens dos testes existentes)

**Resposta:** Sim, adoção completa (Recomendado)

Opções apresentadas:

- **Sim, adoção completa (Recomendado)** ✓ **ESCOLHIDA**
  - Endpoints do LogStream ganham RequirePermission (log-entries:read/write etc.); SDK traz resolver por configuração (Secco:Authorization:Roles:<role>:Permissions) para DEV/testes, e o Secco.SecureGate.Client traz o resolver remoto. E2E da fase: client com role → LogStream autoriza via permissões resolvidas no SecureGate; revogar a permissão → 403 em ≤ 1 TTL. Mantém o padrão 'cada fase prova o recurso num produto real'.
- **Só o SDK nesta fase**
  - AddSeccoAuthorization() + APIs do SecureGate + resolver remoto prontos, mas nenhum produto aplica policies ainda — adoção fica para a 6.5 (login de usuário), quando haverá humanos com roles de verdade. Menos mudança agora, porém o recurso fecha a fase sem prova E2E — quebra o padrão das fases anteriores.

---

## Rodada 26

### 72. [Stack de UI] Qual stack para as telas de login/logout (e futura consent)? É a primeira UI HTML da plataforma, até aqui 100% API-first.

**Resposta:** Razor Pages (Recomendado)

Opções apresentadas:

- **Razor Pages (Recomendado)** ✓ **ESCOLHIDA**
  - Host interativo canônico do OpenIddict + Identity: SignInManager pluga direto, sem build de SPA/JS no pipeline, server-rendered com antiforgery nativo. As telas ficam no Secco.SecureGate.Api (poucas páginas: Login, Logout). Menor superfície e menos dependências (ADR-0020).
- **HTML estático + minimal endpoints**
  - Páginas HTML servidas por minimal endpoints, POST do form tratado à mão com SignInManager. Zero framework de view, mas reescreve antiforgery/model binding/validação que o Razor já dá — mais código de borda para o mesmo resultado.
- **Blazor Server**
  - Componentes interativos com circuito SignalR. Poderoso para o AdminPortal (Fase 7), mas exagero para 2–3 telas de login e adiciona um modelo de execução com estado (circuito/websocket) ao servidor de identidade — mais para operar e proteger.

### 73. [Tenant no login] Como o login descobre o tenant do usuário? Hoje o schema tem email/username único GLOBAL (decisão da 6.1).

**Resposta:** Email global → tenant derivado (Recomendado)

Opções apresentadas:

- **Email global → tenant derivado (Recomendado)** ✓ **ESCOLHIDA**
  - Mantém o schema atual: o usuário digita email+senha, o tenant vem do registro (User.TenantId) — sem seletor de tenant, sem descoberta ambígua. Honra a decisão registrada da 6.1. Trade-off consciente: um mesmo email pertence a UM tenant (uma pessoa = uma organização); multi-tenant por pessoa fica como extensão futura se houver demanda real.
- **Email por tenant + tenant no request**
  - Unicidade de email passa a ser por (tenant, email) — mesma correção que fiz nos roles; o tenant chega na requisição de autorização (parâmetro/slug) e a tela de login opera naquele contexto. Permite a mesma pessoa em vários tenants, mas exige mudança de schema+migration e o client precisa informar o tenant antes do login. Contradiz a decisão/comentário da 6.1.
- **Tenant por subdomínio**
  - acme.securegate.… identifica o tenant antes do login; email único por tenant. UX limpa em produção, mas exige wildcard DNS/TLS e resolução de host — infra que a plataforma ainda não tem e que complica DEV/testes; melhor decidir junto do AdminPortal.

### 74. [Provisionamento] Como usuários são criados nesta fase?

**Resposta:** Provisionado por admin (Recomendado)

Opções apresentadas:

- **Provisionado por admin (Recomendado)** ✓ **ESCOLHIDA**
  - Sem auto-registro público: usuários são criados via API sob scope securegate:admin (o AdminPortal e operadores gerenciam), com hash de senha do Identity. Coerente com tenants/roles já sendo admin-managed; menor superfície de ataque (sem cadastro anônimo, ADR-0020). Seed de DEV cria um usuário demo para exercitar o login.
- **Auto-registro (self-service)**
  - Tela pública de cadastro que cria usuário + (qual tenant?). Abre superfície anônima (rate limit, verificação de email, anti-abuso) e esbarra na questão de a que tenant o novo usuário pertence — prematuro antes do AdminPortal definir o onboarding.

### 75. [Consent] Tela de consentimento (consent) para o authorization code agora?

**Resposta:** Pular p/ first-party, adiar tela (Recomendado)

Opções apresentadas:

- **Pular p/ first-party, adiar tela (Recomendado)** ✓ **ESCOLHIDA**
  - O AdminPortal (único client de curto prazo) é first-party confiável → ConsentType Implicit no OpenIddict: sem tela de consent. A tela só é construída quando existir um client de terceiros real — mesmo critério de 'backlog até demanda real' das fases anteriores (YAGNI). Refresh token/offline_access habilitado (o AdminPortal precisa de sessão durável).
- **Construir a tela de consent agora**
  - Fluxo OAuth completo com consent explícito desde já, inclusive para first-party. Mais fiel ao protocolo, mas adiciona uma tela + persistência de consentimento que nenhum client atual exercita — código sem consumidor no momento.

---

## Rodada 27

### 76. [Stack] Qual stack para o AdminPortal? É uma UI interativa (grids, filtros, formulários), não uma API — primeira do gênero na plataforma.

**Resposta:** Blazor Server (Recomendado)

Opções apresentadas:

- **Blazor Server (Recomendado)** ✓ **ESCOLHIDA**
  - C# ponta a ponta: reusa os clients NSwag (Secco.*.Client) direto, sem build JS nem BFF; tokens custodiados no SERVIDOR (nunca no browser, ADR-0020); interatividade rica (grids/filtros/paginacão) nativa. Trade-off: circuito SignalR com estado — um novo modelo de execucão a operar/proteger. Ideal para console interno de baixa escala.
- **Razor Pages / MVC**
  - Server-rendered request/response, o mais simples e sem circuito com estado. Porém grids/filtros interativos exigem JavaScript e plumbing manual que o Blazor dá de graça — mais código de borda para o mesmo resultado num console admin.
- **Blazor WebAssembly + BFF**
  - Sensacão de SPA, mas exige um Backend-for-Frontend para custodiar tokens (não expô-los no browser) + um segundo processo para operar. Mais pecãs e superfície para um console interno; melhor quando há exigência real de app offline/rich client.

### 77. [Chamadas] Como o AdminPortal chama as APIs de produto (LogStream, SecureGate)?

**Resposta:** Token do operador (on-behalf-of) (Recomendado)

Opções apresentadas:

- **Token do operador (on-behalf-of) (Recomendado)** ✓ **ESCOLHIDA**
  - O AdminPortal loga o operador via authorization code + PKCE (Fase 6.5) e chama as APIs COM o token DELE: cada acão carrega a identidade e as permissões reais (ADR-0021), e o produto autoriza no seu próprio boundary; a auditoria é a pessoa. Exige que o token do operador traga os scopes/audiences necessários (securegate:admin, logstream, catalog:*, authorization:read).
- **Token de serviço (client credentials)**
  - O AdminPortal age como máquina com scopes amplos. Mais simples de configurar, mas perde a autorizacão POR USUÁRIO no produto e o rastro de auditoria vira 'o AdminPortal', não a pessoa — contra o least-privilege da ADR-0020/0021.

### 78. [Operador] Quem é o usuário do AdminPortal?

**Resposta:** Console de operador cross-tenant (Recomendado)

Opções apresentadas:

- **Console de operador cross-tenant (Recomendado)** ✓ **ESCOLHIDA**
  - É o console do OPERADOR da plataforma: cria/gerencia tenants, provisiona usuários/roles em qualquer tenant e navega logs de qualquer tenant (um seletor de tenant dirige as views per-tenant). Coerente com 'gestão de tenants' (plural). O operador porta securegate:admin. Nuance a resolver na ADR: criar tenant é acão de plataforma, acima de um tenant — provavelmente um tenant 'plataforma' para operadores.
- **Admin por tenant**
  - Cada admin gerencia só o próprio tenant; autorizacão mais simples, mas NÃO cobre criar/gerenciar tenants (acão de plataforma) — empurraria o provisionamento de tenants para fora do AdminPortal, contradizendo o objetivo da fase.

### 79. [1ª entrega] Escopo da primeira entrega (7.1)?

**Resposta:** Fundacão + login + shell + 1 fatia fina (Recomendado)

Opções apresentadas:

- **Fundacão + login + shell + 1 fatia fina (Recomendado)** ✓ **ESCOLHIDA**
  - 7.1 = scaffold do produto + login OIDC (prova o authorization code/PKCE da 6.5) + shell autenticado (layout, seletor de tenant, health/logout) + UMA fatia real fim-a-fim (lista de tenants via SecureGate.Client) para provar o padrão. Depois: 7.2 identidade (users/roles/permissões), 7.3 logs por tenant, 7.4 gestão de bancos de tenant. Incremental, cada fatia com testes.
- **Slice maior na 1ª entrega**
  - 7.1 já com login + gestão completa de tenants + usuários de uma vez. Menos idas, porém primeira entrega grande, mais difícil de revisar e com mais chance de retrabalho antes do padrão estar provado.

---

## Rodada 28

### 80. [Navegação] Como o operador acessa a identidade de um tenant? (usuários e roles são POR tenant)

**Resposta:** Drill-in: página de detalhe do tenant (Recomendado)

Opções apresentadas:

- **Drill-in: página de detalhe do tenant (Recomendado)** ✓ **ESCOLHIDA**
  - Clicar na lista de tenants (7.1) abre /tenants/{id} com seções Usuários e Roles. O contexto de tenant fica explícito na URL, reusa a lista da 7.1 como ponto de entrada e é o modelo natural para identidade por-tenant. Deep-link/refresh funcionam.
- **Seletor global + páginas no menu**
  - Um tenant 'ativo' escolhido num seletor de topo dirige páginas dedicadas Usuários/Roles no menu. Menos cliques para alternar entre seções do mesmo tenant, mas o contexto vira estado implícito (some da URL, refresh/deep-link perdem o tenant).

### 81. [Usuários] Profundidade da gestão de USUÁRIOS na 7.2?

**Resposta:** Listar + criar (reusa endpoints existentes) (Recomendado)

Opções apresentadas:

- **Listar + criar (reusa endpoints existentes) (Recomendado)** ✓ **ESCOLHIDA**
  - Sem novos endpoints no SecureGate: lista usuários do tenant e cria (e-mail + senha inicial + roles). Cobre o essencial do provisionamento. Editar roles de um usuário e desativar entram numa fatia posterior, quando houver os endpoints — mantém a 7.2 focada no AdminPortal.
- **Também editar roles + desativar**
  - Gestão completa do usuário, mas exige NOVOS endpoints no SecureGate (ex.: PUT users/{id}/roles, desativar/lockout) — amplia a fase para Domain/Application/Api/contrato/testes do SecureGate além das telas. Mais retrabalho de revisão numa entrega só.

### 82. [Permissões] Como editar as PERMISSÕES de um role? (PUT idempotente — conjunto completo)

**Resposta:** Texto livre (uma permissão recurso:acao por linha) (Recomendado)

Opções apresentadas:

- **Texto livre (uma permissão recurso:acao por linha) (Recomendado)** ✓ **ESCOLHIDA**
  - Textarea; o AdminPortal envia o conjunto no PUT idempotente. Sem novo endpoint — a API já valida o formato recurso:acao e rejeita inválidas (400). Simples e suficiente; um seletor com catálogo entra se houver demanda real.
- **Seletor com catálogo de permissões conhecidas**
  - Checkboxes a partir do universo de permissões registradas. Melhor descoberta/validação, MAS exige um endpoint novo no SecureGate que liste as permissões — hoje elas são constantes em CADA produto (ADR-0003), não há catálogo central; expô-lo é uma decisão de arquitetura à parte.

---

## Rodada 29

### 83. [Acesso] Como o operador lê logs de QUALQUER tenant, dado o conflito de tenancy (claim vs header) e o gate por permissão por tenant?

**Resposta:** Token do operador, tenant-less para dados (Recomendado)

Opções apresentadas:

- **Token do operador, tenant-less para dados (Recomendado)** ✓ **ESCOLHIDA**
  - O token do operador de plataforma NÃO carrega claim tenant_id (operador não é 'de' um tenant de dados) — ele escolhe o tenant por requisição via X-Tenant-Id, usando o caminho 'sem claim → header' que a ADR-0005 JÁ permite (sem reformar a regra de conflito). A autorização concede ao papel platform-operator um conjunto READ-ONLY em qualquer tenant. Preserva auditoria por operador (o sub é a pessoa) e é DRY entre produtos. Custo: capacidade 'super-leitor' (só leitura) ampla, registrada em ADR.
- **Token de serviço read-only (client credentials)**
  - A leitura usa uma identidade de MÁQUINA do AdminPortal (caminho serviço-a-serviço, sem claim de tenant). Separa a capacidade de leitura da identidade do operador, mas a auditoria NO PRODUTO vira 'o AdminPortal' (mitigado por log no AdminPortal). Também precisa de um papel distinguido com read-set. Não preserva o on-behalf-of que guiou 7.1/7.2.

### 84. [Read-set] Onde o conjunto READ-ONLY do operador (log-entries:read etc.) é definido?

**Resposta:** Resolução especial no SecureGate (Recomendado)

Opções apresentadas:

- **Resolução especial no SecureGate (Recomendado)** ✓ **ESCOLHIDA**
  - A resolução role→permissions do SecureGate reconhece o papel distinguido (platform-operator) e devolve um read-set fixo (log-entries:read, log-processes:read, api-call-logs:read) para QUALQUER tenant. A autoridade de IAM decide (ADR-0021), produtos e SDK ficam inalterados, e há UM lugar para evoluir o read-set. O produto (LogStream) não sabe que é operador — só recebe permissões.
- **Papel semeado por tenant**
  - Um papel com as permissões de leitura é semeado em CADA tenant no provisionamento; resolução normal por (tenant, papel). Sem caso especial, mas espalha um conceito de plataforma no espaço de papéis de todo tenant e concede leitura a quem for atribuído a ele dentro do tenant — mais superfície e ruído.

### 85. [Fatia] Escopo da tela de logs na 7.3 (quais logs primeiro)?

**Resposta:** Log geral (log-entries) primeiro (Recomendado)

Opções apresentadas:

- **Log geral (log-entries) primeiro (Recomendado)** ✓ **ESCOLHIDA**
  - A fatia fim-a-fim: listar/buscar log-entries de um tenant (a partir do drill-in), paginado, com filtros básicos (nível, período). Prova o padrão de leitura cross-tenant com o menor escopo; processos e ApiCallLogs entram como incrementos depois.
- **Os três de uma vez (entries + processos + api-call-logs)**
  - Cobertura completa dos três tipos de log numa entrega. Mais útil de imediato, porém tela maior e mais superfície para revisar antes de o padrão cross-tenant estar provado.

---

## Rodada 30

### 86. [Produto] Como o operador informa o PRODUTO ao cadastrar/rotacionar o banco de um tenant?

**Resposta:** Texto livre (Recomendado)

Opções apresentadas:

- **Texto livre (Recomendado)** ✓ **ESCOLHIDA**
  - Um input de produto (kebab-case, ex.: logstream); a API já valida o formato (400 se inválido). Sem novo endpoint e consistente com a edição de permissões da 7.2. Ao 'rotacionar' um banco existente, o campo vem pré-preenchido a partir da lista de Products do tenant.
- **Lista fixa de produtos conhecidos**
  - Dropdown com os produtos multi-tenant (hoje: logstream). Menos erro de digitação, mas a lista precisa vir de algum lugar — ou hardcoded no AdminPortal (acopla ao catálogo de produtos) ou de um endpoint novo no SecureGate que liste os produtos com scope catalog:* semeados. Mais superfície para um ganho pequeno agora.

### 87. [Escopo] Escopo da gestão de bancos na 7.4?

**Resposta:** Cadastrar/rotacionar (reusa o endpoint) (Recomendado)

Opções apresentadas:

- **Cadastrar/rotacionar (reusa o endpoint) (Recomendado)** ✓ **ESCOLHIDA**
  - Mostra os produtos que têm banco (lista Products do tenant, sem revelar connection strings — write-only, ADR-0020) e permite cadastrar/rotacionar via o PUT idempotente existente. Cobre o essencial do provisionamento; remover banco entra depois, quando houver o endpoint. Mantém a 7.4 só no AdminPortal.
- **Também remover/descadastrar banco**
  - Gestão completa, mas exige um endpoint NOVO no SecureGate (DELETE do banco de um tenant/produto) — amplia a fase para Domain/Application/Api/contrato/testes do SecureGate. Remover um banco é destrutivo (o produto perde a resolução do tenant) — melhor tratar com cuidado numa fatia própria.

