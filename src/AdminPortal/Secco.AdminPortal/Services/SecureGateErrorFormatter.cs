using System.Text.Json;
using Secco.SecureGate.Client;

namespace Secco.AdminPortal.Services;

/// <summary>
/// Traduz uma <see cref="ApiException"/> do client do SecureGate numa mensagem amigável
/// para o operador, extraindo o <c>detail</c> do ProblemDetails quando disponível. Não
/// expõe corpo bruto nem stack traces (ADR-0020).
/// </summary>
public static class SecureGateErrorFormatter
{
    /// <summary>Descreve a falha para exibição na UI.</summary>
    /// <param name="exception">Exceção lançada pelo client gerado.</param>
    public static string Describe(ApiException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception.StatusCode)
        {
            case 401:
                return "Sessão expirada ou sem autorização. Faça login novamente.";
            case 403:
                return "Você não tem permissão para esta operação.";
        }

        if (TryReadProblemDetail(exception.Response) is { Length: > 0 } detail)
        {
            return detail;
        }

        return $"Não foi possível concluir a operação (HTTP {exception.StatusCode}).";
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
