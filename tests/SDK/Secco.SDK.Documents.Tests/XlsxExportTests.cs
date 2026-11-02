using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using Xunit;

namespace Secco.SDK.Documents.Tests;

public sealed class XlsxExportTests : TempRootTest
{
	private async Task<SpreadsheetDocument> ExportAsync(IEnumerable<Linha> linhas)
	{
		var bytes = await ExportFixture.ExportBytesAsync(ExportFixture.Exporter(Root), ExportFixture.Pedido(ExportFormat.Xlsx, linhas));
		return SpreadsheetDocument.Open(new MemoryStream(bytes), false);
	}

	private static List<Row> DataRows(SpreadsheetDocument document)
		=> [.. Sheet(document, "Dados").Descendants<Row>()];

	private static Worksheet Sheet(SpreadsheetDocument document, string name)
	{
		var workbook = document.WorkbookPart!;
		var sheet = workbook.Workbook.Descendants<Sheet>().Single(s => s.Name == name);
		return ((WorksheetPart)workbook.GetPartById(sheet.Id!)).Worksheet;
	}

	private static string Text(Cell cell) => cell.InlineString?.InnerText ?? cell.CellValue?.Text ?? string.Empty;

	[Fact]
	public async Task Planilha_e_valida_pelo_open_xml_sdk()
	{
		using var document = await ExportAsync(ExportFixture.Muitas(50));

		new OpenXmlValidator().Validate(document).Should().BeEmpty();
	}

	[Fact]
	public async Task Cabecalho_e_linhas_saem_na_ordem()
	{
		using var document = await ExportAsync(ExportFixture.Muitas(3));
		var rows = DataRows(document);

		rows.Should().HaveCount(4);
		rows[0].Elements<Cell>().Select(Text).Should().Equal("Texto", "Inteiro", "Número", "Valor", "Data", "Instante");
		Text(rows[3].Elements<Cell>().First()).Should().Be("linha 3");
	}

	[Theory]
	[InlineData("=1+1")]
	[InlineData("+SOMA(A1:A9)")]
	[InlineData("-2+3")]
	[InlineData("@SUM(A1)")]
	[InlineData("=HYPERLINK(\"https://atacante.test\",\"clique\")")]
	public async Task Texto_que_parece_formula_continua_texto(string hostil)
	{
		using var document = await ExportAsync([new Linha(hostil, null, null, null, null, null)]);
		var cell = DataRows(document)[1].Elements<Cell>().First();

		cell.DataType!.Value.Should().Be(CellValues.InlineString);
		cell.InlineString!.InnerText.Should().Be(hostil);
		document.WorkbookPart!.WorksheetParts.SelectMany(p => p.Worksheet.Descendants<CellFormula>()).Should().BeEmpty();
	}

	[Fact]
	public async Task Caractere_de_controle_e_surrogate_solto_sao_removidos_e_emoji_fica()
	{
		using var document = await ExportAsync([new Linha("a\u0001b\u0000c\uD800d😀", null, null, null, null, null)]);

		Text(DataRows(document)[1].Elements<Cell>().First()).Should().Be("abcd😀");
		new OpenXmlValidator().Validate(document).Should().BeEmpty();
	}

	[Fact]
	public async Task Texto_acima_do_limite_da_celula_e_cortado_com_marcador()
	{
		using var document = await ExportAsync([new Linha(new string('x', 40_000), null, null, null, null, null)]);
		var text = Text(DataRows(document)[1].Elements<Cell>().First());

		text.Should().HaveLength(32_767).And.EndWith("…");
	}

	[Fact]
	public async Task Espaco_nas_pontas_e_preservado()
	{
		using var document = await ExportAsync([new Linha("  recuado ", null, null, null, null, null)]);

		Text(DataRows(document)[1].Elements<Cell>().First()).Should().Be("  recuado ");
	}

	[Fact]
	public async Task Numeros_e_datas_sao_celulas_numericas_e_o_instante_sai_no_fuso()
	{
		var instante = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
		using var document = await ExportAsync([new Linha(null, 42, 3.5m, 1234.56m, new DateOnly(2026, 10, 1), instante)]);
		var cells = DataRows(document)[1].Elements<Cell>().ToList();

		cells[1].CellValue!.Text.Should().Be("42");
		cells[2].CellValue!.Text.Should().Be("3.5");
		cells[3].CellValue!.Text.Should().Be("1234.56");
		double.Parse(cells[4].CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture)
			.Should().Be(new DateTime(2026, 10, 1).ToOADate());

		// 12:00 UTC é 09:00 em São Paulo.
		DateTime.FromOADate(double.Parse(cells[5].CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture))
			.Should().BeCloseTo(new DateTime(2026, 10, 1, 9, 0, 0), TimeSpan.FromSeconds(1));
		cells.Skip(1).Should().OnlyContain(c => c.StyleIndex != null && c.StyleIndex.Value > 0);
	}

	[Fact]
	public async Task Moeda_usa_o_simbolo_da_cultura()
	{
		using var document = await ExportAsync(ExportFixture.Muitas(1));
		var formats = document.WorkbookPart!.WorkbookStylesPart!.Stylesheet.NumberingFormats!.Elements<NumberingFormat>();

		formats.Select(f => f.FormatCode!.Value).Should().Contain("\"R$\" #,##0.00");
	}

	[Fact]
	public async Task Aba_sobre_traz_titulo_tenant_filtros_e_emissao()
	{
		using var document = await ExportAsync(ExportFixture.Muitas(1));
		var lines = Sheet(document, "Sobre").Descendants<Row>()
			.Select(r => string.Join("|", r.Elements<Cell>().Select(Text)))
			.ToList();

		lines.Should().Contain("Título|Trilha de auditoria")
			.And.Contain("Tenant|Acme Indústria")
			.And.Contain("Ator|maria@acme.test")
			.And.Contain("Emitido por|Maria Operadora");
		lines.Should().Contain(l => l.StartsWith("Emitido em|01/11/2026 12:30:00 (America/Sao_Paulo)", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Sem_linhas_sai_so_o_cabecalho_e_continua_valida()
	{
		using var document = await ExportAsync([]);

		DataRows(document).Should().HaveCount(1);
		new OpenXmlValidator().Validate(document).Should().BeEmpty();
	}
}
