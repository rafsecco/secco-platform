using System.Security.Claims;
using Secco.SecureGate.Application;

namespace Secco.SecureGate.Api.Authorization;

/// <summary>
/// Autorização por scope OAuth (Fase 6.3): o JWT emitido pelo próprio SecureGate carrega a
/// claim curta <c>scope</c> (ADR-0007) — um valor por claim ou lista separada por espaço,
/// conforme a RFC 8693. As permissions granulares <c>recurso:ação</c> da ADR-0021 chegam na
/// fase 6.4; scopes continuam sendo a fronteira de CLIENT (o que a aplicação pode chamar).
/// </summary>
internal static class ScopeAuthorization
{
    /// <summary>Verifica se o principal carrega o scope exigido (comparação ordinal exata).</summary>
    /// <param name="user">Principal autenticado da requisição.</param>
    /// <param name="requiredScope">Scope exigido (ex.: <c>securegate:admin</c>).</param>
    public static bool HasScope(ClaimsPrincipal user, string requiredScope) =>
        user.FindAll("scope")
            .SelectMany(static claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(requiredScope, StringComparer.Ordinal);

    /// <summary>
    /// Filtro de endpoint do catálogo: exige o scope <c>catalog:&lt;produto&gt;</c> do produto
    /// da PRÓPRIA ROTA — least privilege (ADR-0020): o client de um produto não lê connection
    /// strings de outro. Comparação ordinal — produto malicioso simplesmente não casa com
    /// nenhum scope concedido.
    /// </summary>
    /// <param name="context">Contexto de invocação do endpoint.</param>
    /// <param name="next">Próximo delegate do pipeline de filtros.</param>
    public static async ValueTask<object?> RequireCatalogScopeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var product = context.HttpContext.Request.RouteValues["product"] as string;

        return !string.IsNullOrEmpty(product)
            && HasScope(context.HttpContext.User, SecureGateScopes.CatalogFor(product.ToLowerInvariant()))
            ? await next(context).ConfigureAwait(false)
            : Results.Forbid();
    }
}
