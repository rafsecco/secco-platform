# Secco.SDK.Imaging

Tratamento seguro de imagem enviada por usuário (ADR-0042): recebe os bytes do upload e devolve imagens normalizadas — validadas pelo conteúdo, sem metadados, com a orientação do EXIF aplicada, recortadas e redimensionadas em variantes nomeadas. O SkiaSharp fica atrás de uma porta interna; nenhum tipo dele aparece na API pública.

A SDK trata só bytes. Armazenamento, tenant e autorização são do produto.

> **Não use em upload anônimo.** A decodificação roda no próprio processo, porque o caso previsto é upload de usuário autenticado da própria empresa. Upload anônimo exige ADR nova que reavalie o isolamento em processo separado (ADR-0042).
>
> **Alpine (musl) fica fora do v1:** o asset nativo referenciado é o de glibc.

## Uso

```csharp
services.AddSeccoImaging("Intranet:Imaging");
```

```jsonc
{
  "Intranet": {
    "Imaging": {
      "MaxInputBytes": 10485760,
      "MaxPixels": 50000000,
      "MaxConcurrentOperations": 4
    }
  }
}
```

Todas as chaves são opcionais; os padrões estão na tabela abaixo. Valores menores que 1 derrubam o startup.

```csharp
public sealed class FotoService(ISeccoImageProcessor imagens)
{
    public async Task<Result<ProcessedImage>> ProcessarAsync(Stream upload, CancellationToken ct)
    {
        var pedido = new ImageProcessingRequest(
            Variants:
            [
                new ImageVariant("avatar", 64),
                new ImageVariant("perfil", 512),
            ]);

        return await imagens.ProcessAsync(upload, pedido, ct);
    }
}
```

Com recorte escolhido pelo usuário e saída JPEG:

```csharp
var pedido = new ImageProcessingRequest(
    Variants: [new ImageVariant("perfil", 512)],
    Crop: new CropRectangle(X: 120, Y: 40, Width: 600, Height: 600),
    Format: ImageOutputFormat.Jpeg,
    Quality: 85);
```

O resultado é um dicionário por nome de variante (comparação ordinal); cada `ImageOutput` traz `Content`, `ContentType`, `Width` e `Height`.

## Opções

| Opção | Padrão | O que limita |
|---|---|---|
| `MaxInputBytes` | 10 MB | Maior entrada aceita, em bytes. A leitura é limitada e não confia em `Stream.Length`. |
| `MaxPixels` | 50 milhões | Maior área (largura x altura), conferida no **cabeçalho**, antes de alocar o buffer de pixels. |
| `MaxConcurrentOperations` | `Environment.ProcessorCount` | Quantas operações rodam ao mesmo tempo no processo; as demais esperam vaga (e respeitam o cancelamento). |

## Pedido

| Campo | Padrão | Regra |
|---|---|---|
| `Variants` | — | Ao menos uma, com nomes únicos. `MaxSide` de 1 a 4096; a imagem **nunca é ampliada**. |
| `Crop` | quadrado central | Em coordenadas da imagem **já orientada** (depois do EXIF). Fora dos limites: `InvalidCrop`. |
| `Format` | `WebP` | `WebP` ou `Jpeg`; no JPEG, transparência é composta sobre fundo branco. |
| `Quality` | 80 | De 1 a 100. |

Pedido inválido (variantes vazias ou repetidas, lado ou qualidade fora da faixa) é bug do chamador e lança `ArgumentException`. Erro do arquivo do usuário volta como `Result` (ADR-0004).

Uma chamada produz todas as variantes a partir de **uma única** decodificação: o arquivo não confiável passa pelo decodificador uma vez só.

## Formatos

- **Entrada:** JPEG, PNG e WebP, reconhecidos pelo conteúdo e nunca pela extensão ou pelo `Content-Type` declarado.
- **Saída:** WebP (padrão) ou JPEG, codificados do zero a partir dos pixels, em sRGB, sem nenhum metadado do original (localização do EXIF incluída).
- HEIC e AVIF são reconhecidos pela marca `ftyp` e respondem `UnsupportedFormat` (o SkiaSharp não lê HEIC no Linux; o Safari do iPhone costuma converter para JPEG no envio). Aceitar formato novo de entrada exige rever a ADR-0042.

## Erros

Os erros são `Error.Validation` fixos de `SeccoImagingErrors`; nunca ecoam conteúdo, nome ou metadados do arquivo recebido (ADR-0020).

| Erro | Código | Sugestão de mapeamento |
|---|---|---|
| `InputTooLarge` | `Imaging.Image.InputTooLarge` | `413` |
| `UnsupportedFormat` | `Imaging.Image.UnsupportedFormat` | `415` |
| `InvalidImage` | `Imaging.Image.InvalidImage` | `400` |
| `TooManyPixels` | `Imaging.Image.TooManyPixels` | `400` |
| `InvalidCrop` | `Imaging.Image.InvalidCrop` | `400` |

O mapeamento HTTP é decisão do produto.

## Ao servir o resultado

- Responda com `Content-Type` fixo, vindo do `ContentType` devolvido, e `X-Content-Type-Options: nosniff`.
- O nome do arquivo é gerado pelo servidor; nunca use o nome enviado pelo usuário.
- **Descarte o original.** Nada do arquivo enviado deve ser guardado; para trocar o recorte, a pessoa envia a foto de novo.
