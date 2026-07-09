using Microsoft.AspNetCore.Builder;
using Secco.SDK.AspNetCore.Tenancy;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição de pipeline para multi-tenancy (ADR-0005).</summary>
public static class SeccoTenancyApplicationBuilderExtensions
{
    /// <summary>
    /// Adiciona o <see cref="SeccoTenancyMiddleware"/> ao pipeline. Deve vir <b>após</b>
    /// <c>UseAuthentication()</c> (a claim <c>tenant_id</c> é a fonte primária) e antes
    /// de qualquer middleware/endpoint que acesse dados de tenant.
    /// Requer <c>AddSeccoTenancy()</c> registrado no DI.
    /// </summary>
    /// <param name="app">Pipeline de middlewares da aplicação.</param>
    public static IApplicationBuilder UseSeccoTenancy(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SeccoTenancyMiddleware>();
    }
}
