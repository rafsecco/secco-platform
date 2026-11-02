using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Secco.LogStream.Api.Endpoints;
using Secco.LogStream.Domain.Audit;
using Xunit;

namespace Secco.LogStream.Tests.Integration;

/// <summary>
/// Exportação da trilha (ADR-0058): mesmo filtro e mesmo tenant da busca, permissão própria,
/// limite por pessoa, e a exportação registrada na própria trilha ANTES do envio — se o registro
/// falha, o arquivo não sai.
/// </summary>
public class AuditEntryExportTests(LogStreamApiFactory factory) : IClassFixture<LogStreamApiFactory>, IAsyncLifetime
{
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public async Task InitializeAsync() => await factory.EnsureDatabaseMigratedAsync();

	public Task DisposeAsync() => Task.CompletedTask;

	private HttpClient Client(Guid tenant, string subject, string role = LogStreamApiFactory.DefaultTestRole)
	{
		var client = factory.CreateClient();
		client.DefaultRequestHeaders.Authorization =
			new AuthenticationHeaderValue("Bearer", factory.CreateToken(tenant, subject, role));
		return client;
	}

	private static string Subject() => $"exportador-{Guid.NewGuid():N}";

	private async Task<string> SeedAsync(Guid tenant, int count)
	{
		var actor = $"ator-{Guid.NewGuid():N}";
		using var client = Client(tenant, Subject());

		for (var i = 0; i < count; i++)
		{
			var response = await client.PostAsJsonAsync("/api/v1/audit-entries", new
			{
				actorId = actor,
				actorType = "User",
				action = i == 0 ? "=HYPERLINK(\"https://atacante.test\")" : "documento.download",
				resourceType = "documento",
				resourceId = $"doc-{i}",
			});
			response.StatusCode.Should().Be(HttpStatusCode.Created);
		}

		return actor;
	}

	private static List<List<string>> Rows(byte[] xlsx)
	{
		using var document = SpreadsheetDocument.Open(new MemoryStream(xlsx), false);
		var workbook = document.WorkbookPart!;
		var sheet = workbook.Workbook.Descendants<Sheet>().Single(s => s.Name == "Dados");
		return
		[
			.. ((WorksheetPart)workbook.GetPartById(sheet.Id!)).Worksheet.Descendants<Row>()
				.Select(r => r.Elements<Cell>().Select(c => c.InlineString?.InnerText ?? c.CellValue?.Text ?? string.Empty).ToList()),
		];
	}

	private async Task<List<JsonElement>> ExportRecordsAsync(Guid tenant, string exporter)
	{
		using var client = Client(tenant, Subject());
		var page = await client.GetFromJsonAsync<JsonElement>(
			$"/api/v1/audit-entries?actorId={exporter}&action={ExportAuditEntriesAction}", Json);
		return [.. page.GetProperty("items").EnumerateArray()];
	}

	private const string ExportAuditEntriesAction = Secco.LogStream.Application.Audit.ExportAuditEntriesHandler.ExportAction;

	[Fact]
	public async Task Xlsx_traz_so_o_filtro_pedido_e_registra_a_exportacao_na_trilha()
	{
		var actor = await SeedAsync(factory.TenantAlfa, 3);
		var exporter = Subject();
		using var client = Client(factory.TenantAlfa, exporter);

		var response = await client.GetAsync($"/api/v1/audit-entries/export?format=xlsx&actorId={actor}&timeZone=America/Sao_Paulo");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
		response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
		response.Content.Headers.ContentDisposition.FileNameStar.Should().StartWith("trilha-de-auditoria-").And.EndWith(".xlsx");
		response.Headers.CacheControl!.NoStore.Should().BeTrue();

		var rows = Rows(await response.Content.ReadAsByteArrayAsync());
		rows.Should().HaveCount(4, "cabeçalho mais as 3 entradas do filtro");
		rows.Skip(1).Should().OnlyContain(r => r[2] == actor);
		rows.Should().Contain(r => r[5] == "=HYPERLINK(\"https://atacante.test\")", "a ação hostil sai como texto");

		var records = await ExportRecordsAsync(factory.TenantAlfa, exporter);
		records.Should().ContainSingle();
		records[0].GetProperty("actorType").GetString().Should().Be("User");
		records[0].GetProperty("resourceType").GetString().Should().Be("audit-entries");

		using var metadata = JsonDocument.Parse(records[0].GetProperty("metadata").GetString()!);
		metadata.RootElement.GetProperty("format").GetString().Should().Be("xlsx");
		metadata.RootElement.GetProperty("rows").GetInt32().Should().Be(3);
		metadata.RootElement.GetProperty("filters").GetProperty("actorId").GetString().Should().Be(actor);
	}

	[Fact]
	public async Task Pdf_sai_como_pdf()
	{
		var actor = await SeedAsync(factory.TenantAlfa, 2);
		using var client = Client(factory.TenantAlfa, Subject());

		var response = await client.GetAsync($"/api/v1/audit-entries/export?format=PDF&actorId={actor}");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
		var bytes = await response.Content.ReadAsByteArrayAsync();
		System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
	}

	[Fact]
	public async Task Nao_cruza_tenants()
	{
		var actor = await SeedAsync(factory.TenantAlfa, 2);
		using var client = Client(factory.TenantBeta, Subject());

		var response = await client.GetAsync($"/api/v1/audit-entries/export?format=xlsx&actorId={actor}");

		response.StatusCode.Should().Be(HttpStatusCode.OK);
		Rows(await response.Content.ReadAsByteArrayAsync()).Should().HaveCount(1, "só o cabeçalho: as entradas são do outro tenant");
	}

	[Fact]
	public async Task Sem_permissao_de_exportar_responde_403_mesmo_lendo_a_trilha()
	{
		using var client = Client(factory.TenantAlfa, Subject(), LogStreamApiFactory.AuditReaderRole);

		(await client.GetAsync("/api/v1/audit-entries?size=1")).StatusCode.Should().Be(HttpStatusCode.OK);
		(await client.GetAsync("/api/v1/audit-entries/export?format=xlsx")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
	}

	[Theory]
	[InlineData("format=csv", "InvalidExportFormat")]
	[InlineData("format=xlsx&timeZone=Lua/Base_Tranquilidade", "InvalidTimeZone")]
	[InlineData("format=xlsx&from=2026-10-02T00:00:00Z&to=2026-10-01T00:00:00Z", "InvalidDateRange")]
	public async Task Pedido_invalido_responde_400_sem_ecoar_o_valor(string query, string code)
	{
		using var client = Client(factory.TenantAlfa, Subject());

		var response = await client.GetAsync($"/api/v1/audit-entries/export?{query}");
		var body = await response.Content.ReadAsStringAsync();

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		body.Should().Contain(code).And.NotContain("Lua/Base_Tranquilidade");
	}

	[Fact]
	public async Task Acima_do_limite_do_pdf_responde_400_e_nao_registra()
	{
		var actor = await SeedAsync(factory.TenantAlfa, LogStreamApiFactory.ExportPdfRowLimit + 1);
		var exporter = Subject();
		using var client = Client(factory.TenantAlfa, exporter);

		var response = await client.GetAsync($"/api/v1/audit-entries/export?format=pdf&actorId={actor}");

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		(await response.Content.ReadAsStringAsync()).Should().Contain("TooManyRows");
		(await ExportRecordsAsync(factory.TenantAlfa, exporter)).Should().BeEmpty();
	}

	[Fact]
	public async Task Trilha_que_falha_impede_o_envio_do_arquivo()
	{
		// sub acima do limite do ator: o registro da exportação é recusado, então o arquivo não pode sair.
		var exporter = "x" + new string('a', 300);
		using var client = Client(factory.TenantAlfa, exporter);

		var response = await client.GetAsync("/api/v1/audit-entries/export?format=xlsx&action=nada-casa-com-isto");

		response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
		response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
		(await response.Content.ReadAsStringAsync()).Should().Contain("ActorIdTooLong");
	}

	[Fact]
	public async Task Limite_por_pessoa_responde_429_e_nao_afeta_outra_pessoa()
	{
		var exporter = Subject();
		using var client = Client(factory.TenantAlfa, exporter);

		for (var i = 0; i < LogStreamApiFactory.ExportPermitsPerMinute; i++)
		{
			(await client.GetAsync("/api/v1/audit-entries/export?format=xlsx&action=nada-casa-com-isto"))
				.StatusCode.Should().Be(HttpStatusCode.OK);
		}

		(await client.GetAsync("/api/v1/audit-entries/export?format=xlsx&action=nada-casa-com-isto"))
			.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

		using var other = Client(factory.TenantAlfa, Subject());
		(await other.GetAsync("/api/v1/audit-entries/export?format=xlsx&action=nada-casa-com-isto"))
			.StatusCode.Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public void Token_de_maquina_vira_ator_client_e_de_pessoa_vira_user()
	{
		var machine = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "cli_intranet"), new Claim("client_id", "cli_intranet")], "test"));
		var person = new ClaimsPrincipal(new ClaimsIdentity(
			[new Claim("sub", "0b9d...pessoa"), new Claim("client_id", "secco-adminportal"), new Claim("name", "Maria")], "test"));

		AuditEntryEndpoints.ResolveActor(machine).Should().Be(new Secco.LogStream.Application.Audit.AuditExportActor("cli_intranet", ActorType.Client, null));
		AuditEntryEndpoints.ResolveActor(person).Should().Be(new Secco.LogStream.Application.Audit.AuditExportActor("0b9d...pessoa", ActorType.User, "Maria"));
	}
}
