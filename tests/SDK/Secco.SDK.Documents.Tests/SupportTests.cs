using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Secco.SDK.Documents.Internal;
using Secco.SDK.Documents.Internal.Pdf;
using Xunit;

namespace Secco.SDK.Documents.Tests;

public sealed class SupportTests
{
	[Theory]
	[InlineData("trilha-de-auditoria", "trilha-de-auditoria")]
	[InlineData("Trilha de Auditoria", "trilha-de-auditoria")]
	[InlineData("Exportação de Ações", "exportacao-de-acoes")]
	[InlineData("../../etc/passwd", "etc-passwd")]
	[InlineData("a\"b;c\r\nd", "a-b-c-d")]
	[InlineData("!!!", "exportacao")]
	[InlineData("", "exportacao")]
	public void Prefixo_do_nome_fica_so_com_letras_digitos_e_hifen(string prefix, string expected)
		=> ExportFileName.Sanitize(prefix).Should().Be(expected);

	[Fact]
	public void Prefixo_e_limitado_em_tamanho()
		=> ExportFileName.Sanitize(new string('a', 200)).Should().HaveLength(40);

	[Theory]
	[InlineData("(", @"(\()")]
	[InlineData(")", @"(\))")]
	[InlineData(@"\", @"(\\)")]
	[InlineData("ã", @"(\343)")]
	[InlineData("€", @"(\200)")]
	[InlineData("😀", "(??)")]
	[InlineData("a\nb", "(a?b)")] // controle nunca chega ao conteúdo como byte de controle
	public void Literal_do_pdf_escapa_e_fica_ascii(string text, string expected)
		=> PdfText.Literal(text).Should().Be(expected);

	[Fact]
	public void Literal_de_texto_hostil_nunca_fecha_a_string()
	{
		var literal = PdfText.Literal(@"x) Tj ET BT /F2 60 Tf (y\");

		// Depois do "(" de abertura, todo ")" sem escape é o de fechamento, e só ele existe.
		var unescapedClosers = 0;
		for (var i = 1; i < literal.Length; i++)
		{
			if (literal[i] == '\\')
			{
				i++;
			}
			else if (literal[i] == ')')
			{
				unescapedClosers++;
			}
		}

		unescapedClosers.Should().Be(1);
		literal.Should().EndWith(")");
		literal.ToCharArray().Should().OnlyContain(c => c < 128);
	}

	[Fact]
	public void Largura_da_helvetica_bate_com_as_medidas_conhecidas()
	{
		WinAnsi.Measure("A", 1000, bold: false).Should().Be(667);
		WinAnsi.Measure("@", 1000, bold: false).Should().Be(1015);
		WinAnsi.Measure("@", 1000, bold: true).Should().Be(975);
		WinAnsi.Measure("ã", 1000, bold: false).Should().Be(556);
	}

	[Fact]
	public void Limpeza_mantem_texto_valido_intacto()
	{
		const string valido = "Ação\tcom\nquebra 😀";

		TextSanitizer.Clean(valido).Should().BeSameAs(valido);
	}

	[Fact]
	public void Corte_da_celula_nao_parte_par_de_surrogates()
	{
		var texto = new string('x', 32_765) + "😀😀";

		var cortado = TextSanitizer.ForExcelCell(texto);

		cortado.Length.Should().BeLessThanOrEqualTo(32_767);
		cortado.Should().EndWith("…");
		char.IsHighSurrogate(cortado[^2]).Should().BeFalse();
	}

	[Theory]
	[InlineData("MaxXlsxRows", "0")]
	[InlineData("MaxXlsxRows", "1048576")]
	[InlineData("MaxPdfRows", "0")]
	[InlineData("MaxConcurrentExports", "0")]
	public void Limite_invalido_derruba_a_subida(string key, string value)
	{
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { [$"Produto:Documents:{key}"] = value })
			.Build();
		using var provider = new ServiceCollection()
			.AddSingleton<IConfiguration>(configuration)
			.AddSeccoDocuments("Produto:Documents")
			.BuildServiceProvider();

		var act = () => provider.GetRequiredService<IOptions<SeccoDocumentsOptions>>().Value;

		act.Should().Throw<OptionsValidationException>().WithMessage($"*Produto:Documents:{key}*");
	}

	[Fact]
	public void Registro_resolve_o_exportador_como_singleton()
	{
		var root = Path.Combine(Path.GetTempPath(), "secco-documents-tests", Guid.NewGuid().ToString("N"));
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?> { ["Produto:Documents:TempDirectory"] = root })
			.Build();
		using var provider = new ServiceCollection()
			.AddSingleton<IConfiguration>(configuration)
			.AddSeccoDocuments("Produto:Documents")
			.BuildServiceProvider();

		provider.GetRequiredService<ISeccoDocumentExporter>()
			.Should().BeSameAs(provider.GetRequiredService<ISeccoDocumentExporter>());
	}
}
