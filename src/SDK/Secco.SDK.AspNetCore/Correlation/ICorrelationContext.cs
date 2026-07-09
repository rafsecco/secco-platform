namespace Secco.SDK.AspNetCore.Correlation;

/// <summary>
/// Correlation id da requisição atual (ADR-0004), populado por <see cref="SeccoCorrelationMiddleware"/>.
/// Registrado com tempo de vida <c>Scoped</c> — só tem valor significativo dentro do
/// pipeline de uma requisição HTTP.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// Correlation id da requisição atual, em texto (representação de um <see cref="Guid"/> v7).
    /// Vazio se acessado fora do pipeline HTTP, antes do middleware executar.
    /// </summary>
    string CorrelationId { get; }
}
