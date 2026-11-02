using System.Text;
using FluentAssertions;
using UglyToad.PdfPig;
using Xunit;

namespace Secco.SDK.Documents.Tests;

public sealed class PdfExportTests : TempRootTest
{
	private Task<byte[]> ExportBytesAsync(IEnumerable<Linha> linhas)
		=> ExportFixture.ExportBytesAsync(ExportFixture.Exporter(Root), ExportFixture.Pedido(ExportFormat.Pdf, linhas));

	private async Task<PdfDocument> ExportAsync(IEnumerable<Linha> linhas) => PdfDocument.Open(await ExportBytesAsync(linhas));

	private static string AllText(PdfDocument pdf) => string.Join("\n", pdf.GetPages().Select(p => p.Text));

	[Fact]
	public async Task Pdf_abre_e_traz_cabecalho_linhas_e_rodape()
	{
		using var pdf = await ExportAsync(ExportFixture.Muitas(3));
		var page = pdf.GetPage(1);

		pdf.NumberOfPages.Should().Be(1);
		page.Text.Should().Contain("Trilha de auditoria")
			.And.Contain("Tenant: Acme Indústria")
			.And.Contain("Filtros: Ator: maria@acme.test; Período: 01/10/2026 a 31/10/2026")
			.And.Contain("linha 3")
			.And.Contain("R$ 1.234,56")
			.And.Contain("Emitido por Maria Operadora em 01/11/2026 12:30:00 (America/Sao_Paulo)")
			.And.Contain("Página 1 de");
	}

	[Fact]
	public async Task Muitas_linhas_quebram_em_paginas_com_cabecalho_repetido_e_total_correto()
	{
		using var pdf = await ExportAsync(ExportFixture.Muitas(400));

		pdf.NumberOfPages.Should().BeGreaterThan(5);
		var last = pdf.GetPage(pdf.NumberOfPages);
		last.Text.Should().Contain("(continuação)").And.Contain("Instante").And.Contain($"Página {pdf.NumberOfPages} de");
		AllText(pdf).Should().Contain("linha 1").And.Contain("linha 400");

		// O "de Y" vem do objeto gravado no fim: o total precisa ser o número real de páginas.
		var total = pdf.NumberOfPages.ToString(System.Globalization.CultureInfo.InvariantCulture);
		pdf.GetPage(1).GetWords().TakeLast(4).Select(w => w.Text).Should().Equal("Página", "1", "de", total);
	}

	[Fact]
	public async Task Texto_hostil_aparece_literal_e_nao_vira_operador()
	{
		const string hostil = @"x) Tj ET BT /F2 60 Tf 100 100 Td (INJETADO) Tj ET BT (\";
		using var pdf = await ExportAsync([new Linha(hostil, null, null, null, null, null)]);
		var page = pdf.GetPage(1);

		page.Letters.Should().OnlyContain(l => l.PointSize <= 13.5, "nenhum texto do dado pode ganhar fonte 60");
		page.Text.Should().Contain("x) Tj ET");
	}

	[Fact]
	public async Task Acentos_saem_certos_e_fora_do_winansi_vira_interrogacao()
	{
		using var pdf = await ExportAsync([new Linha("Ação Ç€ “citação” 😀 中", null, null, null, null, null)]);

		pdf.GetPage(1).Text.Should().Contain("Ação Ç€ “citação” ? ?");
	}

	[Fact]
	public async Task Acento_decomposto_e_recomposto()
	{
		var decomposto = "João";
		using var pdf = await ExportAsync([new Linha(decomposto, null, null, null, null, null)]);

		pdf.GetPage(1).Text.Should().Contain("João");
	}

	[Fact]
	public async Task Texto_longo_quebra_em_ate_quatro_linhas_com_reticencias()
	{
		var longo = string.Join(' ', Enumerable.Repeat("palavra", 400));
		using var pdf = await ExportAsync([new Linha(longo, null, null, null, null, null)]);

		var linhasDaCelula = pdf.GetPage(1).GetWords().Count(w => w.Text.StartsWith("palavra", StringComparison.Ordinal));
		pdf.GetPage(1).Text.Should().Contain("…");
		linhasDaCelula.Should().BeLessThan(400);
	}

	[Fact]
	public async Task Sem_linhas_avisa_que_nao_ha_registro()
	{
		using var pdf = await ExportAsync([]);

		pdf.GetPage(1).Text.Should().Contain("Nenhum registro.");
	}

	[Fact]
	public async Task Pdf_nao_tem_acao_link_nem_javascript()
	{
		var bytes = await ExportBytesAsync([new Linha("/JavaScript /OpenAction /URI /Launch", null, null, null, null, null)]);
		using var pdf = PdfDocument.Open(bytes);
		var raw = Encoding.Latin1.GetString(bytes);

		// O dado vai comprimido no conteúdo da página; fora dele, nenhum desses nomes existe no arquivo.
		raw.Should().NotContain("/JavaScript").And.NotContain("/OpenAction").And.NotContain("/URI").And.NotContain("/Launch").And.NotContain("/AA");
		pdf.GetPage(1).Text.Should().Contain("/JavaScript /OpenAction");
	}

	[Fact]
	public async Task Titulo_com_parenteses_no_info_continua_valido()
	{
		var exporter = ExportFixture.Exporter(Root);
		var request = ExportFixture.Pedido(ExportFormat.Pdf, ExportFixture.Muitas(1)) with { Title = @"Relatório (teste) \ final" };

		var bytes = await ExportFixture.ExportBytesAsync(exporter, request);
		using var pdf = PdfDocument.Open(bytes);

		pdf.Information.Title.Should().Be(@"Relatório (teste) \ final");
	}
}
