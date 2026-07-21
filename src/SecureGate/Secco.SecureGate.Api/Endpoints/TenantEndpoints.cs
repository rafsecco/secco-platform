using Secco.SecureGate.Api.Authorization;
using Secco.SecureGate.Api.Requests;
using Secco.SecureGate.Application;
using Secco.SecureGate.Application.Tenants;
using Secco.SDK.AspNetCore.Extensions;

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
                policy.RequireAssertion(context => ScopeAuthorization.HasScope(context.User, SecureGateScopes.Admin)));

        group.MapPost("/", async (
                CreateTenantRequest request,
                CreateTenantHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(new CreateTenantCommand(request.Name, request.Slug), cancellationToken))
                .ToHttpResult(dto => Results.Created($"/api/v1/tenants/{dto.Id}", dto)))
            .WithName("CreateTenant")
            .WithSummary("Cria um tenant no catálogo da plataforma (nasce ativo, sem bancos cadastrados).")
            .Produces<TenantDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/", async (ListTenantsHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(cancellationToken))
                .ToHttpResult(tenants => Results.Ok(tenants)))
            .WithName("ListTenants")
            .WithSummary("Lista os tenants do catálogo (sem connection strings).")
            .Produces<IReadOnlyList<TenantDto>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, GetTenantHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken))
                .ToHttpResult(dto => Results.Ok(dto)))
            .WithName("GetTenant")
            .WithSummary("Busca um tenant com os produtos que têm banco cadastrado (sem connection strings).")
            .Produces<TenantDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/activate", async (
                Guid id,
                SetTenantActivationHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, active: true, cancellationToken))
                .ToHttpResult(() => Results.NoContent()))
            .WithName("ActivateTenant")
            .WithSummary("Reativa um tenant — volta a ser servido pelo catálogo.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", async (
                Guid id,
                SetTenantActivationHandler handler,
                CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, active: false, cancellationToken))
                .ToHttpResult(() => Results.NoContent()))
            .WithName("DeactivateTenant")
            .WithSummary("Desativa um tenant — some do catálogo em até um TTL de cache dos produtos.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

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
            .WithSummary("Cadastra ou substitui o banco do tenant em um produto (rotação de credencial = novo PUT).")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
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
            .WithSummary("Cadastra ou atualiza o login federado Entra ID do tenant (ADR-0026) — directory id visível na leitura, não é segredo.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
