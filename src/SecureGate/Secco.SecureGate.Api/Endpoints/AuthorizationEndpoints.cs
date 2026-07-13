using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Authorization;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Resolução <c>(tenant, role) → permissões</c> servida aos produtos (ADR-0021) — a fonte
/// que o <c>IPermissionResolver</c> remoto do SDK consome, protegida pelo scope único
/// <c>authorization:read</c>. Role desconhecido responde lista vazia (não 404): para
/// autorização é equivalente, e a resposta não revela o modelo de roles do tenant (ADR-0020).
/// </summary>
public static class AuthorizationEndpoints
{
    /// <summary>Mapeia o endpoint de resolução de permissões.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapAuthorizationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/authorization/tenants/{tenantId:guid}/roles/{role}/permissions", async (
                Guid tenantId,
                string role,
                GetRolePermissionsHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(tenantId, role, cancellationToken))
                .ToHttpResult(permissions => Results.Ok(permissions)))
            .WithTags("Authorization")
            .WithName("GetRolePermissions")
            .WithSummary("Resolve as permissões de um role no tenant (vazio para role desconhecido).")
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization(policy =>
                policy.RequireAssertion(context =>
                    ScopeAuthorization.HasScope(context.User, SecureGateScopes.AuthorizationRead)));

        return endpoints;
    }
}
