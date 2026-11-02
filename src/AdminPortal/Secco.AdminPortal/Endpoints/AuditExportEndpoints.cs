using Microsoft.AspNetCore.Mvc;
using Secco.AdminPortal.Authentication;
using Secco.AdminPortal.Services;
using Secco.SDK.Documents;

namespace Secco.AdminPortal.Endpoints;

/// <summary>Formulário da página de logs. Datas digitadas no fuso do AdminPortal.</summary>
public sealed class AuditExportForm
{
	/// <summary><c>xlsx</c> ou <c>pdf</c>.</summary>
	public string? Format { get; set; }

	/// <summary>A partir de (data e hora local, sem fuso).</summary>
	public DateTime? From { get; set; }

	/// <summary>Até (data e hora local, sem fuso).</summary>
	public DateTime? To { get; set; }

	/// <summary>Ator exato.</summary>
	public string? ActorId { get; set; }

	/// <summary>Ação exata.</summary>
	public string? Action { get; set; }
}

/// <summary>
/// Download da trilha de um tenant (ADR-0058). <c>POST</c> com antiforgery, nunca <c>GET</c>: uma
/// página externa conseguiria disparar um <c>GET</c> com o cookie do operador, gerando trilha falsa
/// e carga. O LogStream gera e registra; aqui o arquivo só é repassado, com nome refeito.
/// </summary>
public static class AuditExportEndpoints
{
	private const string DefaultTimeZone = "America/Sao_Paulo";
	private const int MaxFieldLength = 256;

	/// <summary>Mapeia o download.</summary>
	/// <param name="endpoints">Builder de rotas de endpoints da aplicação.</param>
	public static IEndpointRouteBuilder MapAuditExportEndpoints(this IEndpointRouteBuilder endpoints)
	{
		// [FromForm] faz o pipeline exigir o token antiforgery (UseAntiforgery)
		endpoints.MapPost("/tenants/{tenantId:guid}/audit-entries/export", async (
				Guid tenantId,
				[FromForm] AuditExportForm form,
				IAuditExportService exports,
				IConfiguration configuration,
				TimeProvider time,
				HttpContext httpContext) =>
			{
				var back = $"/tenants/{tenantId}/logs";
				var format = form.Format?.ToUpperInvariant() switch
				{
					"XLSX" => (ExportFormat?)ExportFormat.Xlsx,
					"PDF" => ExportFormat.Pdf,
					_ => null,
				};

				if (format is null || form.ActorId?.Length > MaxFieldLength || form.Action?.Length > MaxFieldLength)
				{
					return Results.LocalRedirect($"{back}?exportError={AuditExportFailure.InvalidRequest}");
				}

				var timeZoneId = configuration[AdminPortalDefaults.TimeZoneKey] ?? DefaultTimeZone;
				var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
				var filter = new AuditExportFilter(
					Instant(form.From, zone),
					Instant(form.To, zone),
					Blank(form.ActorId),
					Blank(form.Action));

				AuditExportFile file;

				try
				{
					file = await exports.ExportAsync(tenantId, format.Value.ToString().ToLowerInvariant(), filter, timeZoneId, httpContext.RequestAborted);
				}
				catch (AuditExportException exception)
				{
					return Results.LocalRedirect($"{back}?exportError={exception.Failure}");
				}

				// O conteúdo é descartado pelo resultado; a resposta HTTP de origem, ao fim da requisição.
				httpContext.Response.RegisterForDisposeAsync(file);
				return SeccoDocumentResults.Attachment(
					file.Content, format.Value, "trilha-de-auditoria", TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone), file.Length);
			})
			.RequireAuthorization(AdminPortalDefaults.OperatorPolicy);

		return endpoints;
	}

	private static DateTimeOffset? Instant(DateTime? local, TimeZoneInfo zone) =>
		local is { } value
			? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), zone.GetUtcOffset(DateTime.SpecifyKind(value, DateTimeKind.Unspecified)))
			: null;

	private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
