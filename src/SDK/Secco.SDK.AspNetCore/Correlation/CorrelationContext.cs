namespace Secco.SDK.AspNetCore.Correlation;

/// <summary>
/// Implementação mutável de <see cref="ICorrelationContext"/>. Pública apenas porque
/// <see cref="SeccoCorrelationMiddleware.InvokeAsync"/> precisa recebê-la via DI (o
/// ASP.NET Core exige assinatura pública em métodos de middleware); o setter é interno
/// para que só o próprio middleware (mesmo assembly) possa definir o valor — o restante
/// da aplicação consome apenas a leitura via <see cref="ICorrelationContext"/>.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    /// <inheritdoc />
    public string CorrelationId { get; internal set; } = string.Empty;
}
