using System.Security.Claims;
using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Api.Tenancy;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Provisioning;
using Secco.SecureGate.Application.Tenants;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Constants;

namespace Secco.SecureGate.Api.Endpoints;

/// <summary>
/// Gestão do catálogo de tenants (<c>/api/v1/tenants</c>) — superfície do AdminPortal
/// (Fase 7) e de operadores. Todo o grupo exige o scope <c>securegate:admin</c> e é
/// write-only para segredos: nenhuma resposta devolve connection strings (ADR-0020).
/// </summary>
public static class TenantEndpoints
{
	/// <summary>Mapeia os endpoints de gestão de tenants.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/tenants")
			.WithTags("Tenants")
			.RequireAuthorization(policy =>
				policy.RequireAssertion(TenantAdminAuthority.AllowsAdminOrMarkedTenantAdmin));

		group.MapPost("/", async (
				CreateTenantRequest request,
				ClaimsPrincipal caller,
				CreateTenantHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new CreateTenantCommand(request.Name, request.Slug, CreatorOf(caller)), cancellationToken))
				.ToHttpResult(dto => Results.Created($"/api/v1/tenants/{dto.Id}", dto)))
			.WithName("CreateTenant")
			.AllowTenantCreator()
			.RequirePlatformScope(scope => scope.EnterPlatform("operação de instalação: cria um tenant"))
			.WithSummary("Cria um tenant no catálogo da plataforma (nasce ativo, sem bancos cadastrados).")
			.Produces<TenantDto>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapGet("/", async (ListTenantsHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(cancellationToken))
				.ToHttpResult(tenants => Results.Ok(tenants)))
			.WithName("ListTenants")
			.RequirePlatformScope(scope => scope.EnterPlatform("operação de instalação: lista os tenants e seus produtos"))
			.WithSummary("Lista os tenants do catálogo (sem connection strings).")
			.Produces<IReadOnlyList<TenantDto>>(StatusCodes.Status200OK);

		group.MapGet("/{id:guid}", async (Guid id, GetTenantHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(id, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.WithName("GetTenant")
			.AllowTenantAdmin()
			.RequireTenantScope("id")
			.WithSummary("Busca um tenant com os produtos que têm banco cadastrado (sem connection strings).")
			.Produces<TenantDetailDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPost("/{id:guid}/activate", async (
				Guid id,
				ClaimsPrincipal caller,
				SetTenantActivationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(id, active: true, TenantAdminAuthority.IsTenantAdminCaller(caller), cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("ActivateTenant")
			.AllowTenantAdmin()
			.RequireTenantScope("id")
			.WithSummary("Reativa um tenant — volta a ser servido pelo catálogo.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPost("/{id:guid}/deactivate", async (
				Guid id,
				ClaimsPrincipal caller,
				SetTenantActivationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(id, active: false, TenantAdminAuthority.IsTenantAdminCaller(caller), cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("DeactivateTenant")
			.AllowTenantAdmin()
			.RequireTenantScope("id")
			.WithSummary("Desativa um tenant — some do catálogo em até um TTL de cache dos produtos, e seus usuários param de renovar token.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapPut("/{id:guid}/databases/{product}", async (
				Guid id,
				string product,
				UpsertTenantDatabaseRequest request,
				UpsertTenantDatabaseHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new UpsertTenantDatabaseCommand(id, product, request.ConnectionString), cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("UpsertTenantDatabase")
			.RequireTenantScope("id")
			.WithSummary("Cadastra ou substitui o banco do tenant em um produto (rotação de credencial = novo PUT).")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPost("/{id:guid}/databases/{product}/provisioning", async (
				Guid id,
				string product,
				ProvisionTenantDatabaseRequest request,
				ClaimsPrincipal caller,
				ProvisionTenantDatabaseHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new ProvisionTenantDatabaseCommand(
					id, product, request.Target, request.Server,
					request.CreateDatabase, request.DatabaseName, request.LoginName,
					Restricted: TenantAdminAuthority.IsTenantAdminCaller(caller)),
				cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.WithName("ProvisionTenantDatabase")
			.AllowTenantAdmin()
			.RequireTenantScope("id")
			.WithSummary("Provisiona o banco dedicado do tenant no produto: cria (ou assume) o database, cria o usuário de aplicação com db_owner APENAS nele e grava a connection string cifrada. Sem credencial privilegiada configurada, devolve o script para um DBA aplicar.")
			.Produces<TenantDatabaseProvisioningDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound)
			.ProducesProblem(StatusCodes.Status409Conflict);

		group.MapGet("/{id:guid}/databases/status", async (
				Guid id,
				GetTenantDatabaseStatusHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(id, cancellationToken))
				.ToHttpResult(statuses => Results.Ok(statuses)))
			.WithName("GetTenantDatabaseStatus")
			.AllowTenantAdmin()
			.RequireTenantScope("id")
			.WithSummary("Estado dos bancos do tenant: o que está cadastrado e o que responde. Usa a conexão de runtime de cada tenant — nenhuma credencial privilegiada.")
			.Produces<IReadOnlyList<TenantDatabaseStatusDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapPut("/{id:guid}/federation", async (
				Guid id,
				UpsertTenantFederationRequest request,
				UpsertTenantFederationHandler handler,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(
				new UpsertTenantFederationCommand(id, request.DirectoryId ?? Guid.Empty, request.Enabled ?? true),
				cancellationToken))
				.ToHttpResult(() => Results.NoContent()))
			.WithName("UpsertTenantFederation")
			.RequireTenantScope("id")
			.WithSummary("Cadastra ou atualiza o login federado Entra ID do tenant (ADR-0026) — directory id visível na leitura, não é segredo.")
			.Produces(StatusCodes.Status204NoContent)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.ProducesProblem(StatusCodes.Status404NotFound);

		return endpoints;
	}

	// Criação pela autoridade de admin de tenant (ADR-0047): o tenant nasce administrado por quem o criou. O
	// filtro da rota já conferiu a concessão de criação; o operador cria sem concessão automática
	private static Guid? CreatorOf(ClaimsPrincipal caller) =>
		TenantAdminAuthority.IsTenantAdminCaller(caller)
		&& Guid.TryParse(caller.FindFirstValue(SeccoClaims.Subject), out var userId)
			? userId
			: null;
}
