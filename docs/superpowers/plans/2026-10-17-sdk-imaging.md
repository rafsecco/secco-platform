# Secco.SDK.Imaging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** criar o pacote `Secco.SDK.Imaging`. Ele recebe uma imagem enviada por usuário e devolve variantes normalizadas, sem metadados e com orientação corrigida, com defesas de recurso aplicadas antes de decodificar.

**Architecture:**
- O `SeccoImageProcessor` público orquestra: leitura limitada, reconhecimento de HEIC/AVIF, sondagem do cabeçalho, limite de pixels, validação do recorte, vaga de concorrência e renderização.
- O SkiaSharp fica atrás da porta interna `IImageCodec`. O adaptador `SkiaImageCodec` sonda o cabeçalho e renderiza as variantes a partir de uma única decodificação.
- Nenhum tipo do SkiaSharp aparece na API pública.

**Tech Stack:** .NET 10, SkiaSharp 4.153.1 + `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.153.1, `Secco.SharedKernel` (`Result`/`Error`), xUnit + FluentAssertions 7 + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-10-17-sdk-imaging-design.md` (ADR-0042). Quem executa lê os dois.

## Global Constraints

**Estilo e build**
- O código segue o estilo do repo: indentação por **tab**, namespace por arquivo e XML doc em todo membro público (`GenerateDocumentationFile`).
- Build Release trata warning como erro.
- Versões só no `Directory.Packages.props` (ADR-0011); `PackageReference` vai sem `Version`.

**API pública**
- Nenhum tipo do SkiaSharp na API pública. A porta `IImageCodec` e o adaptador são `internal`, com `InternalsVisibleTo` para o projeto de teste e para `DynamicProxyGenAssembly2` (NSubstitute).
- Erro de input do usuário é `Result` com `Error.Validation` e um dos 5 códigos: `Imaging.Image.InputTooLarge`, `Imaging.Image.UnsupportedFormat`, `Imaging.Image.InvalidImage`, `Imaging.Image.TooManyPixels`, `Imaging.Image.InvalidCrop`.
- As mensagens de erro não ecoam conteúdo do arquivo nem texto de exceção do decodificador.

**Padrões e validação**
- Padrões das opções: `MaxInputBytes = 10 * 1024 * 1024`, `MaxPixels = 50_000_000`, `MaxConcurrentOperations = Environment.ProcessorCount`.
- Uso errado pelo programador lança `ArgumentException`. São os casos de:
  - lista de variantes vazia;
  - nome de variante vazio ou repetido (comparação ordinal);
  - `MaxSide` fora de 1..4096;
  - `Quality` fora de 1..100.
- Entrada: JPEG, PNG e WebP. Saída: WebP (`image/webp`, padrão) ou JPEG (`image/jpeg`). Sem ampliação.
- O recorte é dado em coordenadas da imagem **já orientada**. Sem recorte informado, vale o quadrado central.

## Review Focus

1. **PNG transparente com saída JPEG:** a área transparente sai **branca**, não preta. O teste fica na Task 3.
2. **Imagem muito alongada** (4000×1) com recorte não quadrado: nenhuma dimensão de saída pode arredondar para 0. O teste fica na Task 3.
3. **Recorte que só cabe na imagem orientada** (200×100 com Orientation 6 e retângulo `(0,100,100,100)`): é aceito. O teste fica na Task 2.
4. **Limite exato:** arquivo com exatamente `MaxInputBytes` bytes e imagem com exatamente `MaxPixels` pixels são aceitos; um a mais é recusado. Os testes ficam na Task 2.
5. **PNG em escala de cinza, com paleta e de 16 bits:** decodifica e sai normalmente. O teste fica na Task 3.

---

### Task 1: Esqueleto do pacote, tipos públicos, opções e registro

**Files:**
- Create: `src/SDK/Secco.SDK.Imaging/Secco.SDK.Imaging.csproj`
- Create: `src/SDK/Secco.SDK.Imaging/ISeccoImageProcessor.cs` (interface e records da requisição e da saída)
- Create: `src/SDK/Secco.SDK.Imaging/SeccoImagingErrors.cs`
- Create: `src/SDK/Secco.SDK.Imaging/SeccoImagingOptions.cs`, `SeccoImagingOptionsValidator.cs`
- Create: `src/SDK/Secco.SDK.Imaging/SeccoImagingServiceCollectionExtensions.cs`
- Create: `src/SDK/Secco.SDK.Imaging/ImageProcessingRequestValidation.cs` (internal, validação de argumento)
- Create: `tests/SDK/Secco.SDK.Imaging.Tests/Secco.SDK.Imaging.Tests.csproj`, `RegistrationTests.cs`, `RequestValidationTests.cs`
- Modify: `Directory.Packages.props` (SkiaSharp 4.153.1 e o asset Linux, com comentário: MIT, ADR-0042), `Secco.Platform.slnx` (os dois projetos), `.github/workflows/publish-packages.yml` (comentário do topo, gatilho `sdk-imaging/v*` e o `case` para o csproj)

**Interfaces:**
- Produces, na API pública (namespace `Secco.SDK.Imaging`):
  - `ISeccoImageProcessor.ProcessAsync(Stream input, ImageProcessingRequest request, CancellationToken cancellationToken = default) : Task<Result<ProcessedImage>>`
  - `record ImageProcessingRequest(IReadOnlyList<ImageVariant> Variants, CropRectangle? Crop = null, ImageOutputFormat Format = ImageOutputFormat.WebP, int Quality = 80)`
  - `record ImageVariant(string Name, int MaxSide)` e `record CropRectangle(int X, int Y, int Width, int Height)`
  - `enum ImageOutputFormat { WebP, Jpeg }`
  - `record ProcessedImage(IReadOnlyDictionary<string, ImageOutput> Variants)` e `record ImageOutput(byte[] Content, string ContentType, int Width, int Height)`
  - `static class SeccoImagingErrors` com `Error InputTooLarge, UnsupportedFormat, InvalidImage, TooManyPixels, InvalidCrop`
  - `class SeccoImagingOptions { long MaxInputBytes; long MaxPixels; int MaxConcurrentOperations; }`
  - `SeccoImagingServiceCollectionExtensions.AddSeccoImaging(this IServiceCollection services, string sectionKey)`, que segue o `AddSeccoEmail`: o pacote entrega os tipos e o produto escolhe a seção (ADR-0033).
- Produces, interno: `ImageProcessingRequestValidation.ThrowIfInvalid(ImageProcessingRequest request)`.

- [ ] **Step 1: csproj.** Use o molde do `Secco.SDK.Email.csproj`:
  - `MinVerTagPrefix` `sdk-imaging/v`, `PackageTags` `secco;sdk;imaging;image;upload`;
  - descrição citando a ADR-0042;
  - `ProjectReference` ao SharedKernel, como no `Secco.SDK.AspNetCore.csproj`;
  - `PackageReference` a `SkiaSharp`, `SkiaSharp.NativeAssets.Linux.NoDependencies`, `Microsoft.Extensions.Options.ConfigurationExtensions` e `Microsoft.Extensions.Logging.Abstractions`;
  - o projeto de teste no molde do `Secco.SDK.Email.Tests.csproj`.
- [ ] **Step 2: testes que falham.**
  - `RegistrationTests`:
    - `AddSeccoImaging_resolve_processador_singleton`: duas resoluções devolvem a mesma instância, porque a vaga de concorrência é compartilhada.
    - `Opcoes_padrao`: 10 MB, 50 milhões e `ProcessorCount`, com a seção vazia.
    - `Opcoes_invalidas_falham_na_validacao`, uma teoria: `MaxInputBytes = 0`, `MaxPixels = 0` e `MaxConcurrentOperations = 0`, cada um lança `OptionsValidationException` ao resolver `IOptions<SeccoImagingOptions>.Value`.
  - `RequestValidationTests` (teoria sobre `ThrowIfInvalid`): lista vazia, nome `""`, nomes `"a"` e `"a"`, `MaxSide` 0 e 4097, `Quality` 0 e 101, cada um lança `ArgumentException`. `MaxSide` 4096 com `Quality` 1 e com 100 não lança.
- [ ] **Step 3: rodar e ver falhar.** `dotnet test tests/SDK/Secco.SDK.Imaging.Tests -c Release` → não compila ou falha.
- [ ] **Step 4: implementar.**
  - O registro faz `AddOptions<SeccoImagingOptions>().BindConfiguration(sectionKey).ValidateOnStart()` + validador.
  - `ISeccoImageProcessor` fica como `TryAddSingleton` para `SeccoImageProcessor`, e `IImageCodec` como `TryAddSingleton` para `SkiaImageCodec`.
  - Nesta task, `SeccoImageProcessor` e `SkiaImageCodec` podem existir como esboço que lança `NotImplementedException`, preenchidos nas Tasks 2 e 3.
- [ ] **Step 5: rodar.** Mesmo comando: passa. `dotnet build Secco.Platform.slnx -c Release` sem warnings.
- [ ] **Step 6: commit.** `feat(sdk-imaging): esqueleto do pacote, tipos publicos e registro (ADR-0042)`

### Task 2: Orquestração e defesas antes da decodificação

**Files:**
- Create: `src/SDK/Secco.SDK.Imaging/Internal/IImageCodec.cs`, `Internal/ImageHeader.cs`, `Internal/BoundedInputReader.cs`, `Internal/IsoBmffSniffer.cs`
- Modify: `src/SDK/Secco.SDK.Imaging/SeccoImageProcessor.cs`
- Modify: `src/SDK/Secco.SDK.Imaging/Internal/SkiaImageCodec.cs` (só o `Probe`)
- Create: `tests/SDK/Secco.SDK.Imaging.Tests/TestImages.cs` (geradores), `NonSeekableStream.cs`, `PreDecodeDefenseTests.cs`

**Interfaces:**
- Produces, interno:
  - `enum ImageInputFormat { Jpeg, Png, WebP }`
  - `record ImageHeader(ImageInputFormat Format, int Width, int Height)`. Largura e altura são **já orientadas**: com Orientation 5–8, as duas trocam.
  - `interface IImageCodec`:
    - `Result<ImageHeader> Probe(ReadOnlyMemory<byte> data)`
    - `Result<IReadOnlyList<RenderedImage>> Render(ReadOnlyMemory<byte> data, CropRectangle crop, IReadOnlyList<ImageVariant> variants, ImageOutputFormat format, int quality, CancellationToken cancellationToken)`
  - `record RenderedImage(string Name, byte[] Content, int Width, int Height)`
  - `internal SeccoImageProcessor(IImageCodec codec, IOptions<SeccoImagingOptions> options, ILogger<SeccoImageProcessor> logger)`. É o construtor usado pelo DI e pelos testes com codec falso.
- Produces, nos testes, `TestImages`:
  - `Png(int w, int h)`, `Jpeg(int w, int h)` e `WebP(int w, int h)`, coloridos com a metade esquerda vermelha e a direita azul;
  - `JpegWithExif(int w, int h, ushort orientation, string marker)`, um APP1 montado à mão logo após o SOI, com cabeçalho TIFF little-endian, IFD0 com `Orientation` (0x0112) e `ImageDescription` (0x010E) contendo `marker`;
  - `PngHeaderOnly(int w, int h)`: assinatura + IHDR com CRC32 correto (CRC em helper próprio) + IDAT mínimo + IEND;
  - `Gif1x1()`, `Bmp1x1()`, `Svg()` e `HeicHeader()` (caixa `ftyp` com marca `heic`).

**Ordem fixa do `ProcessAsync`**, que o teste do codec falso exige:
1. `ThrowIfInvalid` e `ThrowIfCancellationRequested`.
2. `BoundedInputReader` lê até `MaxInputBytes + 1` bytes e recusa com `InputTooLarge` se passar. Usa `Stream.ReadAsync` com o token, sem confiar em `Length`.
3. `IsoBmffSniffer` reconhece `ftyp` com marca `heic`, `heix`, `mif1`, `msf1` ou `avif` e devolve `UnsupportedFormat`.
4. `codec.Probe`.
5. `(long)Width * Height > MaxPixels` devolve `TooManyPixels`.
6. Recorte: sem retângulo, quadrado central com lado `min(W, H)`, `X = (W - lado) / 2` e `Y = (H - lado) / 2`. Com retângulo, `Width` e `Height` > 0, `X` e `Y` ≥ 0, `X + Width ≤ W` e `Y + Height ≤ H`; o contrário devolve `InvalidCrop`.
7. `await semaphore.WaitAsync(cancellationToken)`.
8. `codec.Render`, com `try/finally Release`.
9. Montar o `ProcessedImage` com dicionário ordinal e `ContentType` pelo formato.

- [ ] **Step 1: testes que falham** (`PreDecodeDefenseTests`, codec real salvo onde dito).
  - **Bytes:**
    - `Arquivo_acima_do_limite_em_stream_sem_length`: `NonSeekableStream` com `MaxInputBytes + 1` bytes dá `InputTooLarge`.
    - `Arquivo_com_exatamente_o_limite_passa_da_leitura`: codec falso, PNG preenchido até o limite exato, e `Probe` é chamado.
  - **Formato e conteúdo:**
    - `Heic_e_formato_nao_suportado`.
    - `Gif_e_bmp_sao_recusados` (teoria): `UnsupportedFormat`. O `Probe` do Skia reconhece o formato e recusa o que está fora de JPEG/PNG/WebP.
    - `Svg_texto_e_zero_bytes_sao_imagem_invalida` (teoria): `InvalidImage`.
  - **Pixels:**
    - `Bomba_de_descompressao_e_recusada_pelo_cabecalho`: `PngHeaderOnly(50000, 50000)` com codec real dá `TooManyPixels`.
    - `Bomba_nao_chega_ao_render`: codec falso com `Probe` devolvendo 50000×50000; `Render` não é chamado (`DidNotReceiveWithAnyArgs`).
    - `Pixels_exatamente_no_limite_passam` e `um_a_mais_nao`: codec falso, `MaxPixels = 100`, cabeçalho 10×10 chega ao `Render` e 101×1 não.
  - **Recorte:**
    - `Recorte_fora_dos_limites` (teoria: `(-1,0,10,10)`, `(0,0,0,10)`, `(95,0,10,10)` numa 100×100): `InvalidCrop`, sem `Render`.
    - `Recorte_em_coordenadas_orientadas`: codec real, `JpegWithExif(200, 100, 6, "x")` com `(0,100,100,100)` dá sucesso.
    - `Sem_recorte_usa_quadrado_central`: codec falso com cabeçalho 300×100; `Render` recebe `CropRectangle(100, 0, 100, 100)`.
  - **Erros e cancelamento:**
    - `Erro_nao_ecoa_conteudo`: a `Description` de cada erro devolvido não contém o marcador do arquivo.
    - `Cancelado_antes_lanca`: token já cancelado lança `OperationCanceledException`.
- [ ] **Step 2: rodar e ver falhar.** `dotnet test tests/SDK/Secco.SDK.Imaging.Tests -c Release --filter FullyQualifiedName~PreDecode`.
- [ ] **Step 3: implementar** a orquestração na ordem acima e o `SkiaImageCodec.Probe`.
  - Usar `SKCodec.Create(SKData)`; `null` é `InvalidImage`.
  - Mapear `EncodedFormat` (Jpeg, Png, Webp) e o resto para `UnsupportedFormat`.
  - Dimensões do `Info` com troca quando `EncodedOrigin` for `LeftTop`, `RightTop`, `RightBottom` ou `LeftBottom`.
  - Exceção do Skia vira `InvalidImage`, com log em `Debug` só do tipo da exceção.
- [ ] **Step 4: rodar.** Passa.
- [ ] **Step 5: commit.** `feat(sdk-imaging): defesas antes da decodificacao (ADR-0042)`

### Task 3: Renderização com o Skia — orientação, recorte, redimensionamento, codificação limpa

**Files:**
- Modify: `src/SDK/Secco.SDK.Imaging/Internal/SkiaImageCodec.cs` (`Render`)
- Create: `tests/SDK/Secco.SDK.Imaging.Tests/RenderTests.cs`, `MetadataTests.cs`

**Interfaces:**
- Consumes: `IImageCodec.Render` e `TestImages`, ambos da Task 2.

**O que o `Render` faz:**
- Decodifica uma vez para `SKBitmap` RGBA 8888 premultiplicado em sRGB, convertendo o perfil de cor da fonte.
- Aplica `EncodedOrigin`, desenhando num canvas com a transformação equivalente à orientação.
- Recorta o retângulo, que já vem validado em coordenadas orientadas.
- Para cada variante:
  - `escala = min(1, MaxSide / max(w, h))`;
  - dimensões com `Math.Max(1, (int)Math.Round(...))`;
  - redimensiona com amostragem de alta qualidade;
  - codifica do zero.
- Para JPEG, antes de codificar, compõe sobre fundo **branco** opaco.
- Confere o token entre variantes.
- Decodificação parcial ou falha (`SKCodecResult` diferente de `Success` ou `IncompleteInput`, ou bitmap nulo) dá `InvalidImage`. Imagem truncada também dá `InvalidImage`: `IncompleteInput` conta como falha.
- Se o encoder embutir perfil ICC, a correção é decodificar sem colorspace na saída; os testes de metadados decidem.

- [ ] **Step 1: testes que falham.**
  - **`RenderTests`, formatos e tamanhos:**
    - `Entra_jpeg_png_webp_e_sai_webp` (teoria): `Format` padrão dá `ContentType` `"image/webp"` e o conteúdo começa com `RIFF`, com `WEBP` no offset 8.
    - `Sai_jpeg`: `Format = Jpeg` dá `"image/jpeg"`, com o conteúdo começando por `FF D8`.
    - `Duas_variantes_uma_chamada`: PNG 1000×1000, variantes `avatar` 96 e `perfil` 512, dá 96×96 e 512×512, e a saída decodificada tem essas dimensões.
    - `Nao_amplia`: PNG 300×300 com variante 512 dá 300×300.
  - **`RenderTests`, recorte:**
    - `Recorte_por_retangulo`: PNG 200×100 metade vermelha e metade azul com recorte `(100,0,100,100)`; a saída é toda azul (pixel central, tolerância ±10 por canal).
    - `Quadrado_central`: PNG 300×100 sem recorte dá saída quadrada.
  - **`RenderTests`, orientação:**
    - `Orientacao_aplicada` (teoria: orientação 3, 6 e 8 sobre `JpegWithExif(200, 100, …)`):
      - 6 dá 100×200, com o topo vermelho e a base azul;
      - 8 dá 100×200, com o topo azul e a base vermelha;
      - 3 dá 200×100, com a esquerda azul e a direita vermelha;
      - variante 512, recorte cobrindo a imagem orientada inteira.
  - **`RenderTests`, casos da Review Focus:**
    - `Png_transparente_em_jpeg_fica_branco`: PNG 50×50 totalmente transparente com `Format = Jpeg`; o pixel central é ≥ 245 nos três canais.
    - `Imagem_alongada_nunca_zera_dimensao`: PNG 4000×1 com recorte `(0,0,4000,1)` e variante 96 dá 96×1.
    - `Png_cinza_paleta_16bits` (teoria): cada PNG gerado com `SKColorType.Gray8`, `Alpha8`/paleta e `Rgba16161616` produz saída válida.
    - `Jpeg_truncado_e_invalido`: os primeiros 60% de um JPEG 400×400 dão `InvalidImage`.
  - **`MetadataTests`**, com `JpegWithExif(200, 100, 6, "SECCO-GPS-MARKER")`:
    - `Saida_jpeg_sem_app1_app2`: nenhum `FF E1` nem `FF E2` entre os marcadores, e o ASCII `SECCO-GPS-MARKER` ausente.
    - `Saida_webp_sem_chunks_de_metadado`: nenhum chunk `EXIF`, `XMP ` ou `ICCP`, e o marcador ausente.
- [ ] **Step 2: rodar e ver falhar.** `--filter "FullyQualifiedName~RenderTests|FullyQualifiedName~MetadataTests"`.
- [ ] **Step 3: implementar `SkiaImageCodec.Render`** conforme acima.
- [ ] **Step 4: rodar a suíte do projeto inteira.** Passa.
- [ ] **Step 5: commit.** `feat(sdk-imaging): renderizacao com orientacao, recorte e saida sem metadados (ADR-0042)`

### Task 4: Concorrência limitada e cancelamento durante a espera

**Files:**
- Modify: `src/SDK/Secco.SDK.Imaging/SeccoImageProcessor.cs` (o semáforo, se a Task 2 não o deixou completo)
- Create: `tests/SDK/Secco.SDK.Imaging.Tests/ConcurrencyTests.cs`

**Interfaces:**
- Consumes: o construtor interno do `SeccoImageProcessor` e o `IImageCodec` falso.

- [ ] **Step 1: testes que falham.** `MaxConcurrentOperations = 1` e `Render` falso bloqueando num `ManualResetEventSlim`.
  - `Segunda_chamada_espera_a_primeira`: a 1ª chamada roda em `Task.Run`; a 2ª não chega ao `Render` (contador = 1 após 200 ms). Liberando, as duas concluem com sucesso e o contador fica em 2.
  - `Cancelamento_na_fila_lanca_e_nao_renderiza`: a 2ª chamada com token cancelado em 100 ms lança `OperationCanceledException`, e o `Render` foi chamado uma vez.
  - `Vaga_liberada_apos_falha`: o `Render` falso lança na 1ª chamada; a 2ª chega ao `Render`, o que prova o `finally`.
- [ ] **Step 2: rodar e ver falhar** (`--filter FullyQualifiedName~Concurrency`), ou ver passar se a Task 2 já cobriu. Nesse caso, confirmar por mutação local, removendo o `Release`, que o teste pega.
- [ ] **Step 3: ajustar a implementação** se preciso.
- [ ] **Step 4: rodar a suíte do projeto.** Passa.
- [ ] **Step 5: commit.** `test(sdk-imaging): concorrencia limitada e cancelamento na fila (ADR-0042)`

### Task 5: Documentação de entrega

**Files:**
- Create: `src/SDK/Secco.SDK.Imaging/README.md`, empacotado como no Email. Conteúdo:
  - registro com `AddSeccoImaging("Intranet:Imaging")` e exemplo das duas variantes;
  - tabela das opções com os padrões;
  - os 5 códigos de erro e como mapeá-los (sugestão: `413` para `InputTooLarge`, `400`/`415` para os demais, a critério do produto);
  - formatos de entrada e saída, coordenadas orientadas e quadrado central;
  - **aviso:** não usar em upload anônimo sem ADR nova (ADR-0042); Alpine (musl) fora do v1;
  - ao servir: `Content-Type` fixo pelo `ContentType` devolvido, `X-Content-Type-Options: nosniff`, nome gerado pelo servidor, e descartar o original.
- Modify:
  - `CHANGELOG.md`: em "Não publicado", seção `### Secco.SDK.Imaging 0.1.0 (novo)`, e remover o "_Nada pendente._";
  - `docs/getting-started.md`: linha na tabela de pacotes;
  - `docs/roadmap.md`: item da #29 marcado;
  - `docs/design-decisions-log.md`: entrada da conversa de 2026-10-17 (dois tamanhos, sem refazer recorte, opção A × processo separado, HEIC);
  - `CLAUDE.md`: frase no "Estado atual" e o pacote na lista de versões como "0.1.0 (a publicar)".

- [ ] **Step 1:** escrever. Feito por Sonnet ou pelo revisor, **nunca Haiku**. Só fatos deste plano, do spec e dos commits.
- [ ] **Step 2: verificação completa.** `dotnet build Secco.Platform.slnx -c Release` sem warnings, e `dotnet test Secco.Platform.slnx` com tudo verde (pela solution, não por projeto).
- [ ] **Step 3: commit.** `docs(sdk-imaging): README, changelog e roadmap (issue #29, ADR-0042)`

### Depois das tasks (revisor, não o implementador)

**Mutação:** aplicar, rodar a suíte do projeto e esperar falha, uma a uma:
- limite de pixels conferido depois do `Render`;
- `EncodedOrigin` ignorado;
- `Math.Min(1, …)` removido, ou seja, ampliação permitida;
- checagem de limite do recorte removida;
- fundo branco do JPEG removido;
- `Release` do semáforo removido.

**Release e entrega:**
- Publicar `sdk-imaging/v0.1.0` pela skill `secco-platform-release`, só com confirmação do usuário.
- Comentário de entrega na #29 para a Intranet, e fechamento também com confirmação.
