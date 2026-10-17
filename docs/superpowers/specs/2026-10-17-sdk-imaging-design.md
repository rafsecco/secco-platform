# Secco.SDK.Imaging — design

**Data:** 2026-10-17
**Status:** aprovada (2026-10-17)
**Decisões arquiteturais:** [ADR-0042](../../adr/secco-platform-adrs.md), [ADR-0004](../../adr/secco-platform-adrs.md) (`Result`), [ADR-0011](../../adr/secco-platform-adrs.md) (pacotes e CPM), [ADR-0020](../../adr/secco-platform-adrs.md) (segurança)
**Origem:** issue [#29](https://github.com/rafsecco/secco-platform/issues/29) (`adopter-demand`, `secco-intranet`). A abordagem (porta de codec + SkiaSharp, ImageSharp fora por licença) foi decidida em 2026-09-27 no comentário da issue.

## Por quê

A Intranet quer foto do colaborador no Diretório, e qualquer produto com avatar, logotipo ou anexo de imagem vai precisar do mesmo tratamento. Esse tratamento é fácil de errar: arquivo que finge ser imagem, EXIF com localização (risco de LGPD), imagem pequena em bytes que explode em memória ao decodificar. Por isso ele fica num lugar só, com testes de arquivos malformados. Enquanto a SDK não existe, a Intranet usa avatar por iniciais.

## O que a Intranet faz com a foto (conversa de 2026-10-17)

- A foto aparece em **dois tamanhos**: o avatar pequeno na lista do Diretório e a foto maior no perfil.
- **Não existe recorte refeito.** Para mudar a foto, a pessoa envia outra. A Intranet guarda só as saídas tratadas e descarta o original, ou seja, nada que veio do arquivo enviado fica guardado.

## Objetivos

1. Uma chamada recebe o arquivo enviado, um recorte opcional e a lista de **variantes** pedidas (nome livre + lado máximo), e devolve uma saída por variante: bytes, tipo de conteúdo, largura e altura.
2. O arquivo não confiável passa pelo decodificador **uma vez só**, por mais variantes que se peça.
3. A validação é feita pelo conteúdo, nunca pela extensão nem pelo `Content-Type`. Entram JPEG, PNG e WebP; o resto é recusado com erro tipado.
4. A orientação do EXIF é aplicada e a saída **não carrega nenhum metadado** do original: EXIF, XMP, perfil ICC, comentários.
5. As defesas de recurso agem **antes** da decodificação (limite de bytes, limite de pixels lido do cabeçalho) e ficam valendo durante o processamento (concorrência limitada, cancelamento).
6. A falha causada pelo input do usuário é `Result` com erro tipado, nunca exceção (ADR-0004).

## Não objetivos

- Armazenamento, nome do arquivo, tenant, autorização e headers de resposta: ficam com o produto, como manda a issue.
- Componente de recorte na tela: é UI e fica em cada produto. A SDK recebe só as coordenadas finais.
- HEIC, GIF, SVG, AVIF, TIFF e BMP. HEIC é recusado com erro tipado (o Safari do iPhone costuma converter para JPEG no envio, e o SkiaSharp não lê HEIC no Linux).
- Animação: só o primeiro quadro de PNG ou WebP animado é usado.
- Guardar ou devolver uma versão "mestra" para recortar outra vez.
- Isolar o decodificador em processo separado (descartado na conversa, ver ADR-0042).

## Anti-objetivos (falha mesmo que funcione)

- Uma saída que carregue qualquer byte de metadado do original.
- O produto precisar conhecer o SkiaSharp. Nenhum tipo do SkiaSharp aparece na API pública.
- Alocar o buffer de pixels de uma imagem acima do limite antes de recusá-la.
- Ampliar uma imagem menor que o lado máximo pedido.

## Restrições

- Licença utilizável comercialmente sem cobrança: SkiaSharp é **MIT** (conferido no nuspec da 4.153.1, publicada em 2026-09-30).
- Roda em Windows, Linux e container, sem `System.Drawing`. A SDK referencia `SkiaSharp.NativeAssets.Linux.NoDependencies`, e a imagem Docker do adotante não precisa instalar nada (dispensa fontconfig). Alpine (musl) fica fora do v1 e o README avisa.
- Pacote publicável nos moldes do `Secco.SDK.Email`: tag `sdk-imaging/v`, versões no `Directory.Packages.props`, README empacotado. Depende do `Secco.SharedKernel` (para `Result`/`Error`) e de `Microsoft.Extensions.Options.ConfigurationExtensions`.

## Decidido na conversa

- **Decodificação no próprio processo, com defesas antes e depois** (opção A). O risco residual, uma falha não corrigida no código nativo (libjpeg, libpng, libwebp), chega ao processo do produto. Ele é aceito para upload de usuário autenticado. O isolamento em processo separado só se paga com upload anônimo, e por isso o README da SDK desaconselha usá-la em upload anônimo sem uma ADR nova.
- **Variantes em uma chamada**, com nome livre e significado dado pelo produto.
- **Saída padrão WebP**, com JPEG como opção. A qualidade é configurável por chamada.

## Forma da API (chamada minha)

```csharp
public interface ISeccoImageProcessor
{
    Task<Result<ProcessedImage>> ProcessAsync(
        Stream input, ImageProcessingRequest request, CancellationToken cancellationToken = default);
}

public sealed record ImageProcessingRequest(
    IReadOnlyList<ImageVariant> Variants,
    CropRectangle? Crop = null,                 // null = quadrado central
    ImageOutputFormat Format = ImageOutputFormat.WebP,
    int Quality = 80);                          // 1..100

public sealed record ImageVariant(string Name, int MaxSide);
public sealed record CropRectangle(int X, int Y, int Width, int Height);
public enum ImageOutputFormat { WebP, Jpeg }

public sealed record ProcessedImage(IReadOnlyDictionary<string, ImageOutput> Variants);
public sealed record ImageOutput(byte[] Content, string ContentType, int Width, int Height);
```

- Registro: `services.AddSeccoImaging()` com opções em `Secco:Imaging` (`AddOptions<T>().BindConfiguration()` e validação na subida):
  - `MaxInputBytes`, padrão 10 MB;
  - `MaxPixels`, padrão 50 milhões (cerca de 200 MB de pico em RGBA);
  - `MaxConcurrentOperations`, padrão `Environment.ProcessorCount`.
- O SkiaSharp fica atrás de uma porta **interna** (`IImageCodec`). O processador público orquestra; o adaptador Skia decodifica e codifica. Trocar o codec depois (por exemplo, por decodificação gerenciada) não quebra o adotante.
- `byte[]` na saída, porque as saídas são pequenas (lado máximo limitado, ver abaixo) e o produto as grava direto.

## Comportamento

**Ordem do processamento:**
1. Ler a entrada para um buffer **limitado**: parar e recusar ao passar de `MaxInputBytes`, sem confiar em `Stream.Length`.
2. Abrir o codec só no cabeçalho (`SKCodec`), sem decodificar pixels. Formato fora da lista é `unsupported_format`; conteúdo que não é imagem é `invalid_image`. O Skia não abre HEIC/AVIF no Linux, então a SDK reconhece a caixa `ftyp` (marcas `heic`, `heix`, `mif1`, `avif`) pelos primeiros bytes e responde `unsupported_format`, e não `invalid_image`. Assim o produto pode dizer ao usuário "envie JPEG" em vez de "arquivo inválido".
3. Conferir largura × altura do cabeçalho contra `MaxPixels` antes de alocar o buffer de pixels: `too_many_pixels`.
4. Esperar uma vaga em `MaxConcurrentOperations`, respeitando o cancelamento.
5. Decodificar o primeiro quadro, aplicar a orientação do EXIF e converter para sRGB, RGBA 8 bits. Decodificação que falha no meio, como arquivo truncado, é `invalid_image`.
6. Recortar: retângulo informado ou quadrado central.
7. Para cada variante: redimensionar para o lado máximo sem ampliar, com amostragem de boa qualidade, e codificar do zero a partir dos pixels. O encoder do Skia não escreve metadados, e é isso que garante a saída limpa.
8. Liberar a vaga e devolver o `ProcessedImage`.

**Coordenadas do recorte:** são pixels da imagem **já orientada**. É o que o usuário vê, porque o navegador aplica o EXIF ao exibir. Retângulo vazio, com coordenada negativa ou fora dos limites da imagem orientada é `invalid_crop`. O retângulo não precisa ser quadrado: o lado máximo vale para o maior lado, e a proporção se mantém.

**Cancelamento:** é conferido entre as etapas e lança `OperationCanceledException`, como no resto do .NET. O Skia não interrompe uma decodificação em curso; o limite de pixels é o que mantém essa espera curta.

**Erros tipados** (`Error.Validation`, códigos estáveis em `SeccoImagingErrors`):

| Código | Quando |
|---|---|
| `imaging.input_too_large` | o arquivo passou de `MaxInputBytes` |
| `imaging.unsupported_format` | é imagem, mas não é JPEG, PNG nem WebP (inclui HEIC e GIF) |
| `imaging.invalid_image` | não é imagem, ou a decodificação falhou |
| `imaging.too_many_pixels` | as dimensões do cabeçalho passam de `MaxPixels` |
| `imaging.invalid_crop` | o retângulo é vazio ou sai dos limites |

As mensagens não ecoam conteúdo do arquivo nem detalhes do decodificador. A exceção nativa capturada vai para o log em `Debug`, sem bytes do arquivo.

**Uso errado pelo programador** lança `ArgumentException`, porque não é input do usuário. São os casos de lista de variantes vazia, nome repetido, `MaxSide` fora de 1..4096 e `Quality` fora de 1..100. O teto de 4096 limita o trabalho de codificação.

## Segurança (ADR-0020)

- **Input não confiável:** o conteúdo é validado pelo decodificador; a extensão e o tipo declarado nunca são lidos (a API nem os recebe).
- **Bomba de descompressão:** recusada pelo cabeçalho, antes da alocação. O limite de bytes vale para a leitura e a concorrência limita o pico de memória somado.
- **Metadados e privacidade:** a saída é codificada do zero a partir dos pixels e o original é descartado pelo produto. A Intranet não guarda nada do arquivo original.
- **Código nativo:** risco residual aceito (ver ADR-0042). A mitigação é a versão fixada no `Directory.Packages.props`, o alerta de vulnerabilidade do NuGet (`NuGetAudit`, que já quebra o restore) e a atualização pelo fluxo normal.
- **Vazamento de informação:** os erros não carregam detalhes do decodificador.
- **Dependência nova:** SkiaSharp (MIT, mantido pela Microsoft/.NET Foundation, releases frequentes). Primeira dependência nativa da plataforma fora do runtime.

## Testes (`tests/SDK/Secco.SDK.Imaging.Tests`)

As imagens de teste são geradas no próprio teste com o SkiaSharp, e os arquivos malformados são montados byte a byte, sem binários opacos no repositório.

- **Caminho feliz:** JPEG, PNG e WebP entram; WebP e JPEG saem com o `ContentType` certo; duas variantes saem com as dimensões certas; imagem menor que o lado máximo não é ampliada; recorte por retângulo e quadrado central.
- **Orientação:** JPEG com EXIF `Orientation = 6` e GPS, com o segmento APP1 montado à mão. A saída tem as dimensões giradas e o pixel de canto confere.
- **Sem metadados:** na saída não há segmento APP1/APP2 (JPEG) nem chunk `EXIF`/`XMP `/`ICCP` (WebP), e a string do GPS de teste não aparece.
- **Formatos recusados:** GIF, SVG, BMP, cabeçalho HEIC (`ftypheic`) e texto com extensão `.jpg` resultam em `unsupported_format` ou `invalid_image`.
- **Malformados:** JPEG truncado, PNG com CRC corrompido e zero bytes dão `invalid_image`, sem exceção.
- **Bomba:** PNG pequeno com IHDR declarando 50000×50000 dá `too_many_pixels`. O teste confere que a recusa vem antes da decodificação, por exemplo com um codec falso na porta interna que registra se `Decode` foi chamado.
- **Limite de bytes:** um stream sem `Length` (não pesquisável) maior que o limite dá `input_too_large`.
- **Recorte inválido:** retângulo fora da imagem orientada, com largura zero ou com coordenada negativa.
- **Cancelamento** e **concorrência:** com `MaxConcurrentOperations = 1`, a segunda chamada espera a primeira.
- **Registro:** `AddSeccoImaging()` com opções inválidas falha na validação.
- **Linux:** o job de testes do CI roda em Linux e prova que o asset nativo carrega.
- **Mutação (revisor):** o limite de pixels conferido depois de decodificar, a orientação ignorada, a ampliação permitida e o recorte sem checagem de limites.

## Entrega

- Projeto `src/SDK/Secco.SDK.Imaging` e testes na `Secco.Platform.slnx`. Entrada `sdk-imaging/v*` no `publish-packages.yml`. Versões do SkiaSharp e do asset Linux no `Directory.Packages.props`.
- README do pacote: uso, limites, formatos, o aviso sobre upload anônimo e Alpine, e a recomendação de headers ao servir (`Content-Type` fixo e `X-Content-Type-Options: nosniff`).
- ADR-0042, CHANGELOG, roadmap, design-decisions-log e CLAUDE.md.
- Publicação da `sdk-imaging/v0.1.0` pela skill `secco-platform-release`, com a sua confirmação. Comentário de entrega na #29 para a Intranet.

## Fica com quem constrói

- O filtro de amostragem do redimensionamento (Lanczos ou mitchell, a critério de quem constrói, desde que o resultado fique nítido no avatar pequeno).
- Decodificação em escala reduzida do JPEG (`SKCodec.GetScaledDimensions`) para baixar o pico de memória. É otimização opcional, desde que o limite de pixels continue sendo conferido sobre as dimensões do cabeçalho.
- Organização interna dos tipos e nomes privados.
