using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Roles;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Gestão de roles e permissões por tenant (<c>/api/v1/tenants/{tenantId}/roles</c>,
/// ADR-0021) — superfície do AdminPortal. Todo o grupo exige o scope
/// <c>securegate:admin</c>; a substituição de permissões é um PUT idempotente
/// (revogar = enviar o conjunto sem a permissão — propaga em ≤ 1 TTL de cache).
/// </summary>
public static class RoleEndpoints
{
    /// <summary>Mapeia os endpoints de gestão de roles.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/roles")
            .WithTags("Roles")
            .RequireAuthorization(policy =>
                policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

        group.MapPost("/", async (
                Guid tenantId,
                CreateRoleRequest request,
                CreateRoleHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(new CreateRoleCommand(tenantId, request.Name), cancellationToken))
                .ToHttpResult(dto => Results.Created($"/api/v1/tenants/{tenantId}/roles", dto)))
            .WithName("CreateRole")
            .WithSummary("Cria um role no tenant (nasce sem permissões).")
            .Produces<RoleDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", async (Guid tenantId, ListRolesHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(tenantId, cancellationToken))
                .ToHttpResult(roles => Results.Ok(roles)))
            .WithName("ListRoles")
            .WithSummary("Lista os roles do tenant com suas permissões.")
            .Produces<IReadOnlyList<RoleDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{role}/permissions", async (
                Guid tenantId,
                string role,
                SetRolePermissionsRequest request,
                SetRolePermissionsHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(
                new SetRolePermissionsCommand(tenantId, role, request.Permissions), cancellationToken))
                .ToHttpResult(() => Results.NoContent()))
            .WithName("SetRolePermissions")
            .WithSummary("Substitui o conjunto de permissões do role (idempotente; revogação propaga em até um TTL).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
