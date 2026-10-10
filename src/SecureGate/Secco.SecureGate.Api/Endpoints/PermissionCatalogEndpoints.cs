using Secco.SDK.AspNetCore.Extensions;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Permissions;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Catálogo de permissões publicado pelos produtos (<c>/api/v1/permission-catalog</c>, ADR-0039).
/// A publicação (<c>PUT</c>) exige o scope <c>permissions:&lt;produto&gt;</c> do produto da rota;
/// a leitura exige <c>securegate:admin</c>. Publicar nunca toca perfil.
/// </summary>
public static class PermissionCatalogEndpoints
{
	/// <summary>Mapeia os endpoints do catálogo de permissões.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapPermissionCatalogEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/permission-catalog").WithTags("PermissionCatalog");

		group.MapPut("/{product}", async (
				string product,
				PublishPermissionCatalogRequest request,
				PublishPermissionCatalogHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new PublishPermissionCatalogCommand(
					product,
					request.Permissions?.Select(p => new PermissionDefinitionInput(p?.Name, p?.Description)).ToList()),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.AddEndpointFilter(ScopeAuthorization.RequirePermissionsScopeAsync)
			.WithName("PublishPermissionCatalog")
			.WithSummary("Publica o catálogo de permissões do produto da rota, substituindo o conjunto anterior (escopo permissions:<produto>). Nunca altera perfis.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		var admin = group.MapGroup("/").RequireAuthorization(policy =>
			policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

		admin.MapGet("/", async (
				ListPermissionCatalogHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(cancellationToken))
				.ToHttpResult(catalog => Results.Ok(catalog)))
			.WithName("ListPermissionCatalog")
			.WithSummary("Lista o catálogo de permissões de todos os produtos, com as colisões de nome visíveis.")
			.Produces<IReadOnlyList<ProductPermissionCatalogDto>>(StatusCodes.Status200OK);

		admin.MapGet("/{product}", async (
				string product,
				GetPermissionCatalogHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(product, cancellationToken))
				.ToHttpResult(catalog => Results.Ok(catalog)))
			.WithName("GetPermissionCatalog")
			.WithSummary("Lê o catálogo de permissões de um produto (404 se ele nunca publicou).")
			.Produces<ProductPermissionCatalogDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}
}
