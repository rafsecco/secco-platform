using System.Text.Json;

namespace Secco.LogStream.Application.ApiCalls;

/// <summary>
/// Sanitização defensiva de headers (ADR-0020): o servidor nunca confia no chamador —
/// valores de headers na blocklist são substituídos por <c>[REDACTED]</c> antes de
/// persistir, impedindo que segredos de terceiros parem num banco de logs.
/// </summary>
internal static class HeaderSanitizer
{
    /// <summary>Marcador persistido no lugar de valores sensíveis.</summary>
    public const string RedactedValue = "[REDACTED]";

    private static readonly string[] BuiltInBlocklist =
    [
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "X-Api-Key",
        "Api-Key",
    ];

    /// <summary>
    /// Sanitiza e serializa os headers como JSON, aplicando a blocklist embutida + a
    /// configurada (comparação case-insensitive, como manda o HTTP) e truncando o
    /// resultado no limite de body.
    /// </summary>
    /// <param name="headers">Headers recebidos do chamador; nulo resulta em nulo.</param>
    /// <param name="options">Limites e blocklist adicional.</param>
    public static string? SanitizeAndSerialize(IReadOnlyDictionary<string, string?>? headers, LogStreamIngestionOptions options)
    {
        if (headers is null || headers.Count == 0)
        {
            return null;
        }

        var blocklist = new HashSet<string>(BuiltInBlocklist, StringComparer.OrdinalIgnoreCase);

        foreach (var configured in options.RedactedHeaders)
        {
            blocklist.Add(configured);
        }

        var sanitized = headers.ToDictionary(
            pair => pair.Key,
            pair => blocklist.Contains(pair.Key) ? RedactedValue : pair.Value,
            StringComparer.OrdinalIgnoreCase);

        return BodyTruncator.Truncate(JsonSerializer.Serialize(sanitized), options.MaxBodyLength);
    }
}

/// <summary>Truncamento de corpos no limite configurado, com marcador explícito.</summary>
internal static class BodyTruncator
{
    /// <summary>Sufixo anexado a conteúdos truncados.</summary>
    public const string TruncationMarker = "…[truncated]";

    /// <summary>Trunca o conteúdo no limite, anexando o marcador; nulo/curto passa intacto.</summary>
    /// <param name="content">Conteúdo recebido.</param>
    /// <param name="maxLength">Limite configurado.</param>
    public static string? Truncate(string? content, int maxLength) =>
        content is null || content.Length <= maxLength
            ? content
            : content[..maxLength] + TruncationMarker;
}
