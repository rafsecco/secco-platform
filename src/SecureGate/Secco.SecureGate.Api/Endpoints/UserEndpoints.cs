using System.Security.Claims;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Credentials;
using Secco.SecureGate.Application.Sessions;
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
				new CreateUserCommand(tenantId, request.Email, request.LocalLogin ?? true, request.Roles), cancellationToken))
				.ToHttpResult(dto => Results.Created($"/api/v1/tenants/{tenantId}/users/{dto.Id}", dto)))
			.WithName("CreateUser")
			.WithSummary("Cria um usuário no tenant e envia o convite para ele definir a própria senha (ADR-0033).")
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

		group.MapPost("/{userId:guid}/invite", async (
				Guid tenantId,
				Guid userId,
				InviteUserHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("ResendUserInvite")
			.WithSummary("Reenvia o convite para o usuário definir a primeira senha (ADR-0033).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapPost("/{userId:guid}/password-reset", async (
				Guid tenantId,
				Guid userId,
				AdminResetPasswordHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("ResetUserPassword")
			.WithSummary("Envia o link de redefinição para o usuário e encerra as sessões dele na hora (ADR-0033).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapPost("/{userId:guid}/local-login", async (
				Guid tenantId,
				Guid userId,
				SetLocalLoginRequest request,
				SetLocalLoginHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, request.Enabled, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("SetUserLocalLogin")
			.WithSummary("Liga ou desliga a senha local da conta; desligar apaga a senha e encerra as sessões (ADR-0033).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPost("/{userId:guid}/two-factor/reset", async (
				Guid tenantId,
				Guid userId,
				ResetTwoFactorHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("ResetUserTwoFactor")
			.WithSummary("Zera o cadastro do segundo fator do usuário. Não isenta: o próximo login cadastra de novo.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapDelete("/{userId:guid}/external-logins/{provider}", async (
				Guid tenantId,
				Guid userId,
				string provider,
				RemoveExternalLoginHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, provider, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RemoveUserExternalLogin")
			.WithSummary("Remove o vínculo com um provedor externo (idempotente). Não bloqueia acesso: a federação revincula no próximo login.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

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

		group.MapPost("/{userId:guid}/sessions/revoke", async (
				Guid tenantId,
				Guid userId,
				RevokeUserSessionsHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(tenantId, userId, cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("RevokeUserSessions")
			.WithSummary("Encerra todas as sessões do usuário: produtos recusam os tokens atuais em até um TTL de cache (ADR-0032).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}
}
