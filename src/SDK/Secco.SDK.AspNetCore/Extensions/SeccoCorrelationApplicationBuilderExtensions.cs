using Microsoft.AspNetCore.Builder;
using Secco.SDK.AspNetCore.Correlation;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de pipeline para correlação de requisições (ADR-0004).</summary>
public static class SeccoCorrelationApplicationBuilderExtensions
{
    /// <summary>
    /// Adiciona o <see cref="SeccoCorrelationMiddleware"/> ao pipeline. Deve vir cedo,
    /// antes de qualquer middleware que produza logs ou chamadas a outros produtos.
    /// Requer <c>AddSeccoCorrelation()</c> registrado no DI.
    /// </summary>
    /// <param name="app">Pipeline de middlewares da aplicação.</param>
    public static IApplicationBuilder UseSeccoCorrelation(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SeccoCorrelationMiddleware>();
    }
}
