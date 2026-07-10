using Microsoft.AspNetCore.Builder;

namespace Secco.SDK.AspNetCore.Extensions;

/// <summary>Composição completa de pipeline da plataforma (ADR-0004).</summary>
public static class SeccoPlatformApplicationBuilderExtensions
{
    /// <summary>
    /// Adiciona os middlewares da plataforma na ordem correta: correlação primeiro
    /// (tudo que vem depois loga com correlation id), tenancy em seguida.
    /// Quando o SecureGate existir (Fase 6), a autenticação entrará entre os dois —
    /// a claim <c>tenant_id</c> precisa existir antes do tenancy executar.
    /// Requer <c>AddSeccoPlatform()</c> no DI; completar com <c>MapSeccoPlatform()</c> nos endpoints.
    /// </summary>
    /// <param name="app">Pipeline de middlewares da aplicação.</param>
    public static IApplicationBuilder UseSeccoPlatform(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseSeccoCorrelation();
        // Fase 6: UseAuthentication()/UseAuthorization() entram aqui.
        app.UseSeccoTenancy();

        return app;
    }
}
