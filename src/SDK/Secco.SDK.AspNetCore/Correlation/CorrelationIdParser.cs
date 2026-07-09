namespace Secco.SDK.AspNetCore.Correlation;

/// <summary>
/// Valida o <c>X-Correlation-Id</c> recebido de um chamador externo (ADR-0020: nunca
/// propagar input externo sem validar formato). Só um <see cref="Guid"/> válido e
/// não vazio é reaproveitado; qualquer outra coisa é descartada silenciosamente —
/// o chamador (<see cref="SeccoCorrelationMiddleware"/>) gera um novo id nesse caso.
/// </summary>
internal static class CorrelationIdParser
{
    /// <summary>Tenta interpretar o valor recebido como um correlation id válido.</summary>
    /// <param name="headerValue">Valor bruto do header, possivelmente nulo, vazio ou forjado.</param>
    /// <param name="correlationId">Id interpretado; <see cref="Guid.Empty"/> se inválido.</param>
    public static bool TryParse(string? headerValue, out Guid correlationId)
    {
        if (!string.IsNullOrEmpty(headerValue) && Guid.TryParse(headerValue, out var parsed) && parsed != Guid.Empty)
        {
            correlationId = parsed;
            return true;
        }

        correlationId = Guid.Empty;
        return false;
    }
}
