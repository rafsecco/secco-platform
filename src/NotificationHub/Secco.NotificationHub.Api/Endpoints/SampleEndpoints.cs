using Secco.NotificationHub.Application.Samples;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SharedKernel.Pagination;

namespace Secco.NotificationHub.Api.Endpoints;

/// <summary>Payload de criação de um sample.</summary>
/// <param name="Name">Nome. Obrigatório.</param>
/// <param name="Description">Descrição livre, quando houver.</param>
public sealed record CreateSampleRequest(string? Name, string? Description = null);

/// <summary>
/// Endpoints de EXEMPLO (<c>/api/v1/samples</c>, ADR-0010) — demonstram o padrão da borda:
/// protegidos pela <c>FallbackPolicy</c> (nenhuma metadata necessária), <c>Result&lt;T&gt;</c>
/// convertido via <c>ToHttpResult()</c> (ProblemDetails automático), paginação da plataforma.
/// Apague junto com o restante do recurso Sample.
/// </summary>
public static class SampleEndpoints
{
    /// <summary>Mapeia os endpoints de samples.</summary>
    /// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
    public static IEndpointRouteBuilder MapSampleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/samples").WithTags("Samples");

        group.MapPost("/", async (CreateSampleRequest request, CreateSampleHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(new CreateSampleCommand(request.Name, request.Description), cancellationToken))
                .ToHttpResult(dto => Results.Created($"/api/v1/samples/{dto.Id}", dto)))
            .WithSummary("Cria um sample.")
            .Produces<SampleDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", async (Guid id, GetSampleByIdHandler handler, CancellationToken cancellationToken) =>
            (await handler.HandleAsync(id, cancellationToken))
                .ToHttpResult(dto => Results.Ok(dto)))
            .WithSummary("Busca um sample pelo identificador.")
            .Produces<SampleDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", async (
                SearchSamplesHandler handler,
                CancellationToken cancellationToken,
                string? name,
                int? page,
                int? size) =>
            (await handler.HandleAsync(
                new SampleSearchCriteria(name,
                    new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize)),
                cancellationToken))
                .ToHttpResult(result => Results.Ok(result)))
            .WithSummary("Busca paginada de samples, mais recentes primeiro.")
            .Produces<PagedResult<SampleDto>>(StatusCodes.Status200OK);

        return endpoints;
    }
}
