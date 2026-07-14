using System.Text.Json;

namespace Secco.AdminPortal.Services;

/// <summary>
/// Traduz uma falha de um client NSwag (status + corpo) numa mensagem amigável ao operador,
/// extraindo o <c>detail</c> do ProblemDetails quando disponível. Genérico — serve para os
/// <c>ApiException</c> do SecureGate e do LogStream (tipos distintos, mesmo formato de erro).
/// Não expõe corpo bruto nem stack traces (ADR-0020).
/// </summary>
public static class ApiErrorFormatter
{
    /// <summary>Descreve a falha para exibição na UI.</summary>
    /// <param name="statusCode">Status HTTP da resposta.</param>
    /// <param name="responseBody">Corpo da resposta (ProblemDetails, se houver).</param>
    public static string Describe(int statusCode, string? responseBody)
    {
        switch (statusCode)
        {
            case 401:
                return "Sessão expirada ou sem autorização. Faça login novamente.";
            case 403:
                return "Você não tem permissão para esta operação.";
        }

        if (TryReadProblemDetail(responseBody) is { Length: > 0 } detail)
        {
            return detail;
        }

        return $"Não foi possível concluir a operação (HTTP {statusCode}).";
    }

    private static string? TryReadProblemDetail(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("detail", out var detail)
                && detail.GetString() is { Length: > 0 } detailText)
            {
                return detailText;
            }

            if (document.RootElement.TryGetProperty("title", out var title)
                && title.GetString() is { Length: > 0 } titleText)
            {
                return titleText;
            }
        }
        catch (JsonException)
        {
            // Corpo não-JSON — cai na mensagem genérica
        }

        return null;
    }
}
