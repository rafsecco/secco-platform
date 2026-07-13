using Microsoft.AspNetCore.Builder;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição completa de pipeline da plataforma (ADR-0004).</summary>
public static class SeccoPlatformApplicationBuilderExtensions
{
    /// <summary>
    /// Adiciona os middlewares da plataforma na ordem correta: correlação primeiro
    /// (tudo que vem depois loga com correlation id), autenticação em seguida (ADR-0007),
    /// tenancy antes da autorização — a claim <c>tenant_id</c> precisa existir antes do
    /// tenancy, e o tenant resolvido precisa existir antes das policies de permissão
    /// (ADR-0021, resolução por <c>(tenant, role)</c>) — e autorização por último.
    /// Requer <c>AddSeccoPlatform()</c> no DI; completar com <c>MapSeccoPlatform()</c> nos endpoints.
    /// </summary>
    /// <param name="app">Pipeline de middlewares da aplicação.</param>
    public static IApplicationBuilder UseSeccoPlatform(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSeccoCorrelation();
        app.UseAuthentication();
        app.UseSeccoTenancy();
        app.UseAuthorization();

        return app;
    }
}
