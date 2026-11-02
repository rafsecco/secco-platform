# Secco.SDK.Documents

Exportação de listagens em Excel (`.xlsx`) e PDF tabular, gerada no servidor de cada produto (ADR-0058). Os gravadores são da própria SDK: memória constante, sem dependência de terceiros e sem licença para acompanhar.

A SDK trata só o formato. Buscar os dados, aplicar tenant e autorização, e registrar a trilha de auditoria ficam com o produto.

| Medido | Linhas | Pico de memória | Tempo |
|---|---|---|---|
| Excel | 1 milhão | 55 MB | 4 s |
| PDF | 10 mil (padrão do limite) | 52 MB | 0,7 s |

## Uso

```csharp
services.AddSeccoDocuments("LogStream:Documents");
```

```jsonc
{
  "LogStream": {
    "Documents": {
      "MaxXlsxRows": 100000,
      "MaxPdfRows": 10000,
      "MaxConcurrentExports": 2
    }
  }
}
```

Todas as chaves são opcionais. Valores fora da faixa derrubam a subida.

```csharp
var columns = new ExportColumns<AuditEntryDto>()
    .DateTime("Quando", e => e.OccurredAt)
    .Text("Ator", e => e.ActorId)
    .Text("Ação", e => e.Action, width: 30)
    .WholeNumber("Sequência", e => e.Sequence);

var result = await exporter.ExportAsync(
    new ExportRequest<AuditEntryDto>(ExportFormat.Xlsx, "Trilha de auditoria", "trilha-de-auditoria", columns, rows)
    {
        TenantName = tenant.Name,
        Filters = [new ExportFilter("Ator", actor)],
        IssuedBy = user.Name,
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"),
    },
    httpContext.RequestAborted);

if (result.IsFailure)
{
    // TooManyRows é Error.Validation: vira 400 como qualquer Result da plataforma
    return Result.Failure(result.Error).ToHttpResult(() => Results.NoContent());
}

// Registre a trilha AQUI, antes de enviar: se ela falhar, descarte o documento e não envie.
// Exemplo completo: AuditEntryEndpoints.ExportAsync, no Secco.LogStream.Api.
return result.Value.ToHttpResult();
```

`rows` é um `IAsyncEnumerable<T>`, consumido uma linha por vez, por exemplo um `AsAsyncEnumerable()` do EF Core com os mesmos filtros e o mesmo tenant da listagem.

## O que sai

- **Excel:** aba `Dados` com cabeçalho congelado, autofiltro e células tipadas (número, moeda, data e instante viram número com formato). Aba `Sobre` com título, tenant, filtros e emissão.
- **PDF:** A4 paisagem. O cabeçalho traz título, tenant e filtros, e o cabeçalho da tabela se repete em cada página. O rodapé traz quem emitiu, data/hora e "página X de Y". Texto longo quebra em até 4 linhas e termina em `…`. Números alinham à direita.
  - Fontes padrão do PDF (Helvetica, WinAnsi): todo o português sai certo, e o resultado não depende de fonte instalada. Caractere fora do WinAnsi, como emoji ou CJK, sai como `?`.
- **Fuso e cultura:** os instantes saem no `TimeZone` do pedido (padrão UTC), e o PDF formata na `Culture` do pedido (padrão `pt-BR`).

## Defesas

- **Nenhuma célula vira fórmula:** todo texto é `inlineStr`, e não existe tipo de fórmula na API. `=`, `+`, `-` e `@` continuam texto.
- **PDF sem injeção:** todo texto entra como literal escapado (`\`, `(`, `)` e bytes fora do ASCII em octal). O gravador não tem ação, link nem JavaScript.
- **Dado que quebraria o arquivo:** caractere de controle e surrogate solto são removidos, e texto acima de 32.767 caracteres (o limite da célula) é cortado com `…`.
- **Limites:** linhas por formato (`TooManyRows`), concorrência por processo (as demais esperam vaga) e cancelamento pelo token.
- **Gera primeiro, envia depois:** falha no meio vira exceção, e nunca arquivo truncado com 200.
- **Arquivo temporário:** subdiretório `secco-documents` do temporário (ou de `TempDirectory`). No Linux, o diretório é `0700` e o arquivo `0600`, e a subida recusa um diretório que seja link ou acessível a outros. O arquivo é apagado ao descartar o documento, e restos com mais de uma hora são apagados na subida.
- **Nome do arquivo:** prefixo reduzido a `[a-z0-9-]`, mais a data no fuso do pedido. Nada do usuário chega ao `Content-Disposition`.

Pedido inválido (sem colunas, sem título, formato desconhecido) é bug do chamador e lança `ArgumentException`.

## Resposta HTTP

`ToHttpResult()` envia com `Content-Type` do formato, `Content-Disposition: attachment` (RFC 6266, com `filename*`), `Content-Length`, `Cache-Control: no-store` e `X-Content-Type-Options: nosniff`, e descarta o documento ao terminar. Serve a Minimal APIs e a controllers MVC.

Para repassar um arquivo gerado por outro produto, como um console que baixa pelo client NSwag, use `SeccoDocumentResults.Attachment(stream, formato, prefixo, instante)`. O nome é refeito a partir do prefixo, e nada da resposta de origem vira cabeçalho.

## O que cabe ao produto (ADR-0058)

- **API com Bearer:** `GET /api/v1/<recurso>/export?format=xlsx|pdf&<filtros>`, com os filtros e o isolamento de tenant da listagem. O cliente baixa com `fetch` e `Blob`, nunca com token em query string.
- **Aplicação com cookie:** `POST` com antiforgery, nunca `GET`.
- **Permissão própria** no catálogo (`<recurso>:export`) e **limite de taxa por pessoa**.
- **Trilha síncrona e fail-closed** antes do envio: quem, filtros (com tamanho limitado) e linhas, nunca o conteúdo.
