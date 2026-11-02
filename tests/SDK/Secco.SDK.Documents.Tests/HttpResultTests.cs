using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Secco.SDK.Documents.Tests;

public sealed class HttpResultTests : TempRootTest
{
	private static DefaultHttpContext Context()
	{
		var services = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance).BuildServiceProvider();
		return new DefaultHttpContext { RequestServices = services, Response = { Body = new MemoryStream() } };
	}

	[Fact]
	public async Task Download_sai_com_cabecalhos_seguros_e_descarta_o_temporario()
	{
		var exporter = ExportFixture.Exporter(Root);
		var document = (await exporter.ExportAsync(ExportFixture.Pedido(ExportFormat.Xlsx, ExportFixture.Muitas(2)))).Value;
		var length = document.Length;
		var context = Context();

		await document.ToHttpResult().ExecuteAsync(context);

		var response = context.Response;
		response.StatusCode.Should().Be(200);
		response.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
		response.ContentLength.Should().Be(length);
		response.Headers.ContentDisposition.ToString()
			.Should().StartWith("attachment; filename=trilha-de-auditoria-20261101-123000.xlsx")
			.And.Contain("filename*=UTF-8''trilha-de-auditoria-20261101-123000.xlsx");
		response.Headers.CacheControl.ToString().Should().Be("no-store");
		response.Headers.XContentTypeOptions.ToString().Should().Be("nosniff");
		response.Body.Length.Should().Be(length);
		Directory.GetFiles(StoreDirectory, "*.tmp").Should().BeEmpty();
	}

	[Fact]
	public async Task Repasse_refaz_o_nome_e_fixa_o_tipo_pelo_formato()
	{
		var context = Context();
		var content = new MemoryStream([1, 2, 3]);

		await SeccoDocumentResults.Attachment(content, ExportFormat.Pdf, "Logs do tenant\r\nX-Injetado: 1", ExportFixture.Agora, 3).ExecuteAsync(context);

		context.Response.ContentType.Should().Be("application/pdf");
		context.Response.Headers.ContentDisposition.ToString()
			.Should().StartWith("attachment; filename=logs-do-tenant-x-injetado-1-20261101-153000.pdf")
			.And.NotContain("\r").And.NotContain("\n");
		content.CanRead.Should().BeFalse("o conteúdo é descartado ao terminar");
	}
}
