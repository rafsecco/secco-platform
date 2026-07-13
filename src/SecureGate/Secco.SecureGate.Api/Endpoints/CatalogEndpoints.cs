using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Application.Catalog;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Catálogo de tenants servido aos produtos (<c>/api/v1/catalog/{product}</c>, ADR-0005):
/// a fonte que o <c>ITenantCatalog</c> remoto do SDK consome. Entrega connection strings —
/// cada rota exige o scope <c>catalog:&lt;produto&gt;</c> correspondente (least privilege,
/// ADR-0020): o client de um produto não lê o catálogo de outro.
/// </summary>
public static class CatalogEndpoints
{
    /// <summary>Mapeia os endpoints de catálogo.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/catalog/{product}")
            .WithTags("Catalog")
            .AddEndpointFilter(ScopeAuthorization.RequireCatalogScopeAsync);

        group.MapGet("/tenants", async (
                string product,
                ListCatalogTenantsHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(product, cancellationToken))
                .ToHttpResult(tenants => Results.Ok(tenants)))
            .WithName("ListCatalogTenants")
            .WithSummary("Lista as entradas de catálogo do produto (tenants ativos com banco cadastrado).")
            .Produces<IReadOnlyList<CatalogTenantDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/tenants/{tenantId:guid}", async (
                string product,
                Guid tenantId,
                GetCatalogTenantHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(product, tenantId, cancellationToken))
                .ToHttpResult(entry => Results.Ok(entry)))
            .WithName("GetCatalogTenant")
            .WithSummary("Resolve a entrada de catálogo de um tenant no produto (404 se desconhecido, desativado ou sem banco).")
            .Produces<CatalogTenantDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
