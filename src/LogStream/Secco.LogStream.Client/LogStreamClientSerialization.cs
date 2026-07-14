using System.Text.Json;
using System.Text.Json.Serialization;

namespace Secco.LogStream.Client;

/// <summary>
/// Ajuste manual do client gerado (ADR-0006: partial class FORA da pasta gerada, nunca
/// editando o código gerado). A API do LogStream serializa enums como <b>string</b>
/// (<c>JsonStringEnumConverter</c>), mas o schema OpenAPI não declara <c>type: string</c>
/// no enum — o NSwag então gera um enum numérico sem conversor. Este partial registra o
/// conversor de string para que o client desserialize os enums da API (ex.: níveis de log).
/// Durável a regenerações do client (arquivo separado do gerado).
/// </summary>
public partial class LogStreamClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) =>
        settings.Converters.Add(new JsonStringEnumConverter());
}
