using FluentAssertions;
using Secco.SDK.Documents.Internal;
using Xunit;

namespace Secco.SDK.Documents.Tests;

public sealed class ExporterTests : TempRootTest
{
	private int TempFiles() => Directory.Exists(StoreDirectory) ? Directory.GetFiles(StoreDirectory, "*.tmp").Length : 0;

	[Theory]
	[InlineData(ExportFormat.Xlsx)]
	[InlineData(ExportFormat.Pdf)]
	public async Task Acima_do_limite_recusa_com_erro_tipado_e_nao_deixa_arquivo(ExportFormat format)
	{
		var exporter = ExportFixture.Exporter(Root, o => (o.MaxXlsxRows, o.MaxPdfRows) = (10, 10));

		var result = await exporter.ExportAsync(ExportFixture.Pedido(format, ExportFixture.Muitas(11)));

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SeccoDocumentsErrors.TooManyRows);
		TempFiles().Should().Be(0);
	}

	[Theory]
	[InlineData(ExportFormat.Xlsx)]
	[InlineData(ExportFormat.Pdf)]
	public async Task Exatamente_no_limite_passa(ExportFormat format)
	{
		var exporter = ExportFixture.Exporter(Root, o => (o.MaxXlsxRows, o.MaxPdfRows) = (10, 10));

		var result = await exporter.ExportAsync(ExportFixture.Pedido(format, ExportFixture.Muitas(10)));

		result.IsSuccess.Should().BeTrue();
		await using var document = result.Value;
		document.RowCount.Should().Be(10);
	}

	[Fact]
	public async Task Documento_sai_com_nome_do_servidor_tipo_e_tamanho_e_o_temporario_some_ao_descartar()
	{
		var exporter = ExportFixture.Exporter(Root);

		var result = await exporter.ExportAsync(ExportFixture.Pedido(ExportFormat.Pdf, ExportFixture.Muitas(2)));
		var document = result.Value;

		// 15:30 UTC é 12:30 em São Paulo.
		document.FileName.Should().Be("trilha-de-auditoria-20261101-123000.pdf");
		document.ContentType.Should().Be("application/pdf");
		document.Content.Position.Should().Be(0);
		document.Length.Should().BeGreaterThan(0);
		TempFiles().Should().Be(1);

		await document.DisposeAsync();

		TempFiles().Should().Be(0);
	}

	[Fact]
	public async Task Falha_no_meio_da_geracao_propaga_e_nao_deixa_arquivo()
	{
		var exporter = ExportFixture.Exporter(Root);
		var request = ExportFixture.Pedido(ExportFormat.Xlsx, []) with { Rows = Quebra() };

		var act = () => exporter.ExportAsync(request);

		await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("banco caiu");
		TempFiles().Should().Be(0);

		static async IAsyncEnumerable<Linha> Quebra()
		{
			yield return ExportFixture.Exemplo(1);
			await Task.Yield();
			throw new InvalidOperationException("banco caiu");
		}
	}

	[Fact]
	public async Task Concorrencia_limitada_a_segunda_espera_vaga_e_respeita_cancelamento()
	{
		var exporter = ExportFixture.Exporter(Root, o => o.MaxConcurrentExports = 1);
		var release = new TaskCompletionSource();
		var first = exporter.ExportAsync(ExportFixture.Pedido(ExportFormat.Xlsx, []) with { Rows = Trava(release.Task) });

		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
		var second = () => exporter.ExportAsync(ExportFixture.Pedido(ExportFormat.Xlsx, ExportFixture.Muitas(1)), cts.Token);

		await second.Should().ThrowAsync<OperationCanceledException>();

		release.SetResult();
		await using var document = (await first).Value;
		(await exporter.ExportAsync(ExportFixture.Pedido(ExportFormat.Xlsx, ExportFixture.Muitas(1)))).IsSuccess.Should().BeTrue();

		static async IAsyncEnumerable<Linha> Trava(Task until)
		{
			await until;
			yield return ExportFixture.Exemplo(1);
		}
	}

	[Fact]
	public async Task Cancelar_durante_a_geracao_interrompe_e_nao_deixa_arquivo()
	{
		var exporter = ExportFixture.Exporter(Root);
		using var cts = new CancellationTokenSource();
		var request = ExportFixture.Pedido(ExportFormat.Pdf, []) with { Rows = CancelaNoMeio(cts) };

		var act = () => exporter.ExportAsync(request, cts.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
		TempFiles().Should().Be(0);

		static async IAsyncEnumerable<Linha> CancelaNoMeio(CancellationTokenSource cts)
		{
			yield return ExportFixture.Exemplo(1);
			await cts.CancelAsync();
			yield return ExportFixture.Exemplo(2);
		}
	}

	[Fact]
	public void Restos_de_processo_que_caiu_sao_apagados_na_subida_e_os_recentes_ficam()
	{
		// A SDK cria o diretório (0700 no Linux); um criado aqui sairia 0755 e seria recusado, de propósito.
		_ = ExportFixture.Exporter(Root);
		var velho = Path.Combine(StoreDirectory, "velho.tmp");
		var recente = Path.Combine(StoreDirectory, "recente.tmp");
		File.WriteAllText(velho, "dado de tenant");
		File.WriteAllText(recente, "em uso");
		File.SetLastWriteTimeUtc(velho, ExportFixture.Agora.UtcDateTime.AddHours(-2));
		File.SetLastWriteTimeUtc(recente, ExportFixture.Agora.UtcDateTime.AddMinutes(-5));

		_ = ExportFixture.Exporter(Root);

		File.Exists(velho).Should().BeFalse();
		File.Exists(recente).Should().BeTrue();
	}

	[Fact]
	public async Task No_linux_diretorio_e_arquivo_so_do_dono()
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		var exporter = ExportFixture.Exporter(Root);
		var result = await exporter.ExportAsync(ExportFixture.Pedido(ExportFormat.Xlsx, ExportFixture.Muitas(1)));
		await using var document = result.Value;

		File.GetUnixFileMode(StoreDirectory).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		var file = Directory.GetFiles(StoreDirectory, "*.tmp").Single();
		File.GetUnixFileMode(file).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
	}

	[Fact]
	public void No_linux_diretorio_aberto_a_outros_derruba_a_subida()
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		Directory.CreateDirectory(StoreDirectory);
		File.SetUnixFileMode(StoreDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
			| UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

		var act = () => ExportFixture.Exporter(Root);

		act.Should().Throw<InvalidOperationException>().WithMessage("*acessível a outros*");
	}

	[Fact]
	public async Task Pedido_invalido_e_erro_do_programador()
	{
		var exporter = ExportFixture.Exporter(Root);

		var semColunas = () => exporter.ExportAsync(ExportFixture.Pedido(ExportFormat.Xlsx, []) with { Columns = new ExportColumns<Linha>() });
		var semTitulo = () => exporter.ExportAsync(ExportFixture.Pedido(ExportFormat.Xlsx, []) with { Title = " " });
		var formato = () => exporter.ExportAsync(ExportFixture.Pedido((ExportFormat)99, []));

		await semColunas.Should().ThrowAsync<ArgumentException>();
		await semTitulo.Should().ThrowAsync<ArgumentException>();
		await formato.Should().ThrowAsync<ArgumentException>();
	}

	[Fact]
	public void Coluna_invalida_e_erro_do_programador()
	{
		var columns = new ExportColumns<Linha>();

		((Action)(() => columns.Text(" ", l => l.Texto))).Should().Throw<ArgumentException>();
		((Action)(() => columns.Text("x", null!))).Should().Throw<ArgumentException>();
		((Action)(() => columns.Text("x", l => l.Texto, width: 0))).Should().Throw<ArgumentException>();
	}
}
