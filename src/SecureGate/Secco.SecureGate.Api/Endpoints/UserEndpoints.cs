using System.Security.Claims;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Users;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;

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

		group.MapPost("/{userId:guid}/deactivate", async (
				Guid tenantId,
				Guid userId,
				ClaimsPrincipal caller,
				SetUserActivationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new SetUserActivationCommand(tenantId, userId, Active: false, caller.FindFirst(SeccoClaims.Subject)?.Value),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("DeactivateUser")
			.WithSummary("Desativa um usuário: impede novo login e encerra a sessão na próxima renovação de token.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapPost("/{userId:guid}/activate", async (
				Guid tenantId,
				Guid userId,
				ClaimsPrincipal caller,
				SetUserActivationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new SetUserActivationCommand(tenantId, userId, Active: true, caller.FindFirst(SeccoClaims.Subject)?.Value),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("ActivateUser")
			.WithSummary("Reativa um usuário desativado ou bloqueado por tentativas.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/{userId:guid}", async (
				Guid tenantId,
				Guid userId,
				GetUserHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.WithName("GetUser")
			.WithSummary("Detalha o usuário: situação, perfis, permissões efetivas e logins externos (sem identificadores).")
			.Produces<UserDetailDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPost("/{userId:guid}/roles/{role}", async (
				Guid tenantId,
				Guid userId,
				string role,
				AddUserRoleHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, role, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("AddUserRole")
			.WithSummary("Torna o usuário membro do perfil (idempotente). Vale no token na próxima renovação.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/{userId:guid}/roles/{role}", async (
				Guid tenantId,
				Guid userId,
				string role,
				ClaimsPrincipal caller,
				RemoveUserRoleHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new RemoveUserRoleCommand(tenantId, userId, role, caller.FindFirst(SeccoClaims.Subject)?.Value),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RemoveUserRole")
			.WithSummary("Retira o usuário do perfil (idempotente). Vale no token na próxima renovação.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		return endpoints;
	}
}
