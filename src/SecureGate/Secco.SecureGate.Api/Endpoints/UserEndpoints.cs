using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Users;
using Secco.SDK.AspNetCore.Extensions;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Provisionamento de usuários por tenant (<c>/api/v1/tenants/{tenantId}/users</c>, Fase 6.5).
/// Escopo <c>securegate:admin</c> — usuários são criados por administradores (AdminPortal/
/// operadores), sem auto-registro público (ADR-0020). Senhas nunca voltam nas respostas.
/// </summary>
public static class UserEndpoints
{
    /// <summary>Mapeia os endpoints de gestão de usuários.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/tenants/{tenantId:guid}/users")
            .WithTags("Users")
            .RequireAuthorization(policy =>
                policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

        group.MapPost("/", async (
                Guid tenantId,
                CreateUserRequest request,
                CreateUserHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(
                new CreateUserCommand(tenantId, request.Email, request.Password, request.Roles), cancellationToken))
                .ToHttpResult(dto => Results.Created($"/api/v1/tenants/{tenantId}/users/{dto.Id}", dto)))
            .WithName("CreateUser")
            .WithSummary("Cria um usuário no tenant e atribui os roles informados (senha hasheada no servidor).")
            .Produces<UserDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", async (Guid tenantId, ListUsersHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(tenantId, cancellationToken))
                .ToHttpResult(users => Results.Ok(users)))
            .WithName("ListUsers")
            .WithSummary("Lista os usuários do tenant com seus roles (sem segredos).")
            .Produces<IReadOnlyList<UserDto>>(StatusCodes.Status200OK);

        return endpoints;
    }
}
