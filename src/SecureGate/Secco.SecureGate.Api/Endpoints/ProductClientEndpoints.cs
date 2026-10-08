using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Clients;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Gestão de client de PRODUTO (<c>/api/v1/tenants/{tenantId}/clients</c>, ADR-0037): client
/// <c>client_credentials</c> vinculado ao tenant, cujo token sai com <c>tenant_id</c>. Não existe
/// caminho aqui para client sem tenant — esses só nascem da configuração da instalação.
/// </summary>
public static class ProductClientEndpoints
{
	/// <summary>Mapeia os endpoints de gestão de clients de produto.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapProductClientEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/clients")
			.WithTags("Clients")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

		group.MapPost("/", async (
				Guid tenantId, ProductClientRequest request, CreateProductClientHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, ToCommand(request), cancellationToken))
				.ToHttpResult(created => Results.Created($"/api/v1/tenants/{tenantId}/clients/{created.ClientId}", created)))
			.WithName("CreateProductClient")
			.WithSummary("Registra client de produto vinculado ao tenant. O secret aparece só nesta resposta.")
			.Produces<CreatedProductClientDto>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapGet("/", async (Guid tenantId, ListProductClientsHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, cancellationToken)).ToHttpResult(clients => Results.Ok(clients)))
			.WithName("ListProductClients")
			.WithSummary("Lista os clients de produto do tenant (sem secret).")
			.Produces<IReadOnlyList<ProductClientDto>>(StatusCodes.Status200OK);

		group.MapGet("/{clientId}", async (
				Guid tenantId, string clientId, GetProductClientHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, clientId, cancellationToken)).ToHttpResult(client => Results.Ok(client)))
			.WithName("GetProductClient")
			.WithSummary("Detalha um client de produto do tenant (sem secret).")
			.Produces<ProductClientDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/{clientId}", async (
				Guid tenantId, string clientId, ProductClientRequest request, UpdateProductClientHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, clientId, ToCommand(request), cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("UpdateProductClient")
			.WithSummary("Substitui nome, escopos e papéis do client (idempotente). Não altera o secret.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapPost("/{clientId}/rotate-secret", async (
				Guid tenantId, string clientId, RotateProductClientSecretHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, clientId, cancellationToken)).ToHttpResult(secret => Results.Ok(secret)))
			.WithName("RotateProductClientSecret")
			.WithSummary("Gera secret novo e invalida o anterior na hora. O secret aparece só nesta resposta.")
			.Produces<ProductClientSecretDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/{clientId}", async (
				Guid tenantId, string clientId, DeleteProductClientHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, clientId, cancellationToken)).ToHttpResult(() => Results.NoContent()))
			.WithName("DeleteProductClient")
			.WithSummary("Revoga o client removendo-o. Tokens já emitidos expiram sozinhos.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}

	private static ProductClientCommand ToCommand(ProductClientRequest request) =>
		new(request.Name, request.Scopes, request.Roles);
}
