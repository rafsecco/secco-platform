using System.Security.Claims;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using Secco.LogStream.Api.Requests;
using Secco.LogStream.Application;
using Secco.LogStream.Application.Audit;
using Secco.LogStream.Domain.Audit;
using Secco.SDK.AspNetCore.Correlation;
using Secco.SDK.AspNetCore.Extensions;
using Secco.SDK.AspNetCore.Tenancy;
using Secco.SDK.Documents;
using Secco.SharedKernel.Pagination;
using Secco.SharedKernel.Results;

namespace Secco.LogStream.Api.Endpoints;

/// <summary>
/// Endpoints da trilha de auditoria (<c>/api/v1/audit-entries</c>, ADR-0010). Autorização
/// granular por permissão (ADR-0021): ingestão exige <c>audit-entries:write</c> e consulta
/// <c>audit-entries:read</c>. Sem batch, sem PUT, sem DELETE — uma trilha que se pode editar
/// não é trilha.
/// <para>
/// <b>Ingestão SÍNCRONA — diferença deliberada frente aos outros recursos do LogStream.</b>
/// O POST grava e só então responde <c>201 Created</c>: um registro com obrigação legal não
/// pode desaparecer numa fila cheia sem que ninguém saiba. NÃO trocar por ingestão assíncrona
/// (ver <see cref="CreateAuditEntryHandler"/> e o design doc, seção "Trilha de auditoria").
/// </para>
/// </summary>
public static class AuditEntryEndpoints
{
	/// <summary>Mapeia os endpoints da trilha de auditoria.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapAuditEntryEndpoints(this IEndpointRouteBuilder endpoints)
	{
		var group = endpoints.MapGroup("/api/v1/audit-entries").WithTags("AuditEntries");

		group.MapPost("/", async (
				CreateAuditEntryRequest request,
				CreateAuditEntryHandler handler,
				ICorrelationContext correlation,
				CancellationToken cancellationToken) =>
			(await handler.HandleAsync(ToCommand(request, correlation), cancellationToken))
				.ToHttpResult(dto => Results.Created($"/api/v1/audit-entries/{dto.Id}", dto)))
			.RequireAuthorization(LogStreamPermissions.AuditEntries.Write)
			.WithName("CreateAuditEntry")
			.WithSummary("Registra uma entrada de auditoria (ingestão SÍNCRONA — decisão deliberada, ver documentação do produto).")
			.Produces<AuditEntryDto>(StatusCodes.Status201Created)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		group.MapGet("/{id:guid}", async (Guid id, GetAuditEntryByIdHandler handler, CancellationToken cancellationToken) =>
			(await handler.HandleAsync(id, cancellationToken))
				.ToHttpResult(dto => Results.Ok(dto)))
			.RequireAuthorization(LogStreamPermissions.AuditEntries.Read)
			.WithName("GetAuditEntry")
			.WithSummary("Busca uma entrada de auditoria pelo identificador.")
			.Produces<AuditEntryDto>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status404NotFound);

		group.MapGet("/", async (
				SearchAuditEntriesHandler handler,
				CancellationToken cancellationToken,
				DateTimeOffset? from,
				DateTimeOffset? to,
				string? actorId,
				string? action,
				string? resourceType,
				string? resourceId,
				Guid? correlationId,
				int? page,
				int? size) =>
			(await handler.HandleAsync(
				new AuditEntrySearchCriteria(from, to, actorId, action, resourceType, resourceId, correlationId,
					new PageRequest(page ?? PageRequest.FirstPage, size ?? PageRequest.DefaultSize)),
				cancellationToken))
				.ToHttpResult(result => Results.Ok(result)))
			.RequireAuthorization(LogStreamPermissions.AuditEntries.Read)
			.WithName("SearchAuditEntries")
			.WithSummary("Busca paginada de entradas de auditoria (filtros opcionais, mais recentes primeiro).")
			.Produces<PagedResult<AuditEntryDto>>(StatusCodes.Status200OK)
			.ProducesProblem(StatusCodes.Status400BadRequest);

		group.MapGet("/export", ExportAsync)
			.RequireAuthorization(LogStreamPermissions.AuditEntries.Export)
			.RequireRateLimiting(ExportRateLimiting.Policy)
			.WithName("ExportAuditEntries")
			.WithSummary("Exporta a trilha em Excel (format=xlsx) ou PDF (format=pdf), com os filtros da busca (ADR-0058). A exportação fica registrada na própria trilha antes do envio; se o registro falhar, o arquivo não sai.")
			.Produces(StatusCodes.Status200OK, responseType: null, XlsxContentType, PdfContentType)
			.AddOpenApiOperationTransformer(BinaryDownloadSchema)
			.ProducesProblem(StatusCodes.Status400BadRequest)
			.Produces(StatusCodes.Status429TooManyRequests);

		return endpoints;
	}

	private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
	private const string PdfContentType = "application/pdf";
	private const int MaxTimeZoneLength = 64;

	/// <summary>
	/// Gera, registra e só então envia (ADR-0058): a trilha da exportação é síncrona e fail-closed —
	/// se o registro falhar, o documento é descartado e nada sai.
	/// </summary>
	private static async Task<IResult> ExportAsync(
		HttpContext httpContext,
		ExportAuditEntriesHandler handler,
		ISeccoDocumentExporter exporter,
		ITenantContext tenant,
		ICorrelationContext correlation,
		string format,
		DateTimeOffset? from,
		DateTimeOffset? to,
		string? actorId,
		string? action,
		string? resourceType,
		string? resourceId,
		Guid? correlationId,
		string? timeZone)
	{
		ExportFormat? exportFormat = format.ToUpperInvariant() switch
		{
			"XLSX" => ExportFormat.Xlsx,
			"PDF" => ExportFormat.Pdf,
			_ => null,
		};

		if (exportFormat is null)
		{
			return Problem(LogStreamErrors.AuditEntries.InvalidExportFormat);
		}

		var zone = TimeZoneInfo.Utc;

		if (timeZone is not null
			&& (timeZone.Length > MaxTimeZoneLength || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out zone)))
		{
			return Problem(LogStreamErrors.AuditEntries.InvalidTimeZone);
		}

		var criteria = new AuditEntrySearchCriteria(from, to, actorId, action, resourceType, resourceId, correlationId);
		var rows = handler.Prepare(criteria);

		if (rows.IsFailure)
		{
			return Problem(rows.Error);
		}

		var actor = ResolveActor(httpContext.User);
		var exported = await exporter.ExportAsync(
			new ExportRequest<AuditEntryDto>(exportFormat.Value, "Trilha de auditoria", "trilha-de-auditoria", Columns(), rows.Value)
			{
				TenantName = tenant.TenantId?.ToString(),
				Filters = Filters(criteria),
				IssuedBy = actor.Name ?? actor.Id,
				TimeZone = zone,
			},
			httpContext.RequestAborted);

		if (exported.IsFailure)
		{
			return Problem(exported.Error);
		}

		var document = exported.Value;

		try
		{
			var recorded = await handler.RecordAsync(
				actor,
				format.ToLowerInvariant(),
				document.RowCount,
				criteria,
				Guid.TryParse(correlation.CorrelationId, out var requestCorrelation) ? requestCorrelation : null,
				httpContext.RequestAborted);

			if (recorded.IsFailure)
			{
				await document.DisposeAsync();
				return Problem(recorded.Error);
			}
		}
		catch
		{
			await document.DisposeAsync();
			throw;
		}

		return document.ToHttpResult();
	}

	/// <summary>Ator pelo token: máquina quando o <c>sub</c> é o próprio <c>client_id</c> (client credentials).</summary>
	/// <param name="user">Principal autenticado.</param>
	public static AuditExportActor ResolveActor(ClaimsPrincipal user)
	{
		ArgumentNullException.ThrowIfNull(user);

		var subject = user.FindFirst("sub")?.Value
			?? throw new InvalidOperationException("Token autenticado sem a claim 'sub'.");
		var isClient = string.Equals(subject, user.FindFirst("client_id")?.Value, StringComparison.Ordinal);

		return new AuditExportActor(
			subject,
			isClient ? ActorType.Client : ActorType.User,
			isClient ? null : user.FindFirst("name")?.Value);
	}

	private static ExportColumns<AuditEntryDto> Columns() => new ExportColumns<AuditEntryDto>()
		.DateTime("Ocorrido em", entry => entry.OccurredAt)
		.DateTime("Registrado em", entry => entry.CreatedAt)
		.Text("Ator", entry => entry.ActorId, 28)
		.Text("Nome do ator", entry => entry.ActorName, 22)
		.Text("Tipo", entry => entry.ActorType == ActorType.User ? "Pessoa" : "Máquina", 10)
		.Text("Ação", entry => entry.Action, 26)
		.Text("Recurso", entry => entry.ResourceType, 16)
		.Text("Id do recurso", entry => entry.ResourceId, 24)
		.Text("Metadados", entry => entry.Metadata, 40);

	private static List<ExportFilter> Filters(AuditEntrySearchCriteria criteria)
	{
		var filters = new List<ExportFilter>();
		var culture = System.Globalization.CultureInfo.InvariantCulture;

		void Add(string name, string? value)
		{
			if (value is not null)
			{
				filters.Add(new ExportFilter(name, value));
			}
		}

		Add("De", criteria.From?.ToString("O", culture));
		Add("Até", criteria.To?.ToString("O", culture));
		Add("Ator", criteria.ActorId);
		Add("Ação", criteria.Action);
		Add("Recurso", criteria.ResourceType);
		Add("Id do recurso", criteria.ResourceId);
		Add("Correlação", criteria.CorrelationId?.ToString());

		return filters;
	}

	/// <summary>
	/// Arquivo no contrato como `string`/`binary`: é o que faz o NSwag gerar `FileResponse` no client,
	/// e não um DTO de `Stream`.
	/// </summary>
	private static Task BinaryDownloadSchema(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
	{
		if (operation.Responses?.TryGetValue("200", out var ok) == true && ok is OpenApiResponse response)
		{
			response.Content = new Dictionary<string, OpenApiMediaType>(StringComparer.Ordinal)
			{
				[XlsxContentType] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } },
				[PdfContentType] = new() { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } },
			};
		}

		return Task.CompletedTask;
	}

	private static IResult Problem(Error error) => Result.Failure(error).ToHttpResult(() => Results.NoContent());

	private static CreateAuditEntryCommand ToCommand(CreateAuditEntryRequest request, ICorrelationContext correlation) =>
		new(request.ActorId,
			request.ActorType,
			request.Action,
			request.ActorName,
			request.ResourceType,
			request.ResourceId,
			request.Metadata,
			request.CorrelationId
				?? (Guid.TryParse(correlation.CorrelationId, out var correlationId) ? correlationId : null),
			request.OccurredAt);
}
