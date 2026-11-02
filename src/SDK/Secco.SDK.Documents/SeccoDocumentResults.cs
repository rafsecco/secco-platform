using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using Secco.SDK.Documents.Internal;

namespace Secco.SDK.Documents;

/// <summary>
/// Resposta HTTP de download (ADR-0058). Nenhum produto monta esses cabeçalhos à mão: tipo de mídia
/// fixo pelo formato, <c>Content-Disposition</c> conforme a RFC 6266 com nome gerado no servidor,
/// <c>no-store</c> e <c>nosniff</c>. Serve a Minimal APIs e a controllers MVC.
/// </summary>
public static class SeccoDocumentResults
{
	private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
	private const string PdfContentType = "application/pdf";

	/// <summary>Envia o documento gerado e o descarta ao terminar.</summary>
	/// <param name="document">Documento devolvido por <see cref="ISeccoDocumentExporter"/>.</param>
	public static IResult ToHttpResult(this ExportedDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		return new DownloadResult(document.Content, document.Format, document.FileName, document.Length, ownsContent: true);
	}

	/// <summary>
	/// Repassa um arquivo gerado por outro produto — por exemplo, um console que baixa pelo client
	/// NSwag. O nome é refeito aqui a partir do prefixo; nada da resposta de origem vira cabeçalho.
	/// </summary>
	/// <param name="content">Conteúdo; é descartado ao terminar o envio.</param>
	/// <param name="format">Formato, que fixa o tipo de mídia.</param>
	/// <param name="fileNamePrefix">Prefixo fixo do nome.</param>
	/// <param name="issuedAt">Instante para o nome, no fuso desejado.</param>
	/// <param name="length">Tamanho, se conhecido.</param>
	public static IResult Attachment(Stream content, ExportFormat format, string fileNamePrefix, DateTimeOffset issuedAt, long? length = null)
	{
		ArgumentNullException.ThrowIfNull(content);
		return new DownloadResult(content, format, ExportFileName.Create(fileNamePrefix, format, issuedAt.DateTime), length, ownsContent: true);
	}

	internal static string ContentTypeOf(ExportFormat format) => format == ExportFormat.Pdf ? PdfContentType : XlsxContentType;

	private sealed class DownloadResult(Stream content, ExportFormat format, string fileName, long? length, bool ownsContent) : IResult
	{
		public async Task ExecuteAsync(HttpContext httpContext)
		{
			ArgumentNullException.ThrowIfNull(httpContext);

			try
			{
				var disposition = new ContentDispositionHeaderValue("attachment");
				disposition.SetHttpFileName(fileName);

				var response = httpContext.Response;
				response.StatusCode = StatusCodes.Status200OK;
				response.ContentType = ContentTypeOf(format);
				response.ContentLength = length;
				response.Headers.ContentDisposition = disposition.ToString();
				response.Headers.CacheControl = "no-store";
				response.Headers.XContentTypeOptions = "nosniff";

				await content.CopyToAsync(response.Body, httpContext.RequestAborted).ConfigureAwait(false);
			}
			finally
			{
				if (ownsContent)
				{
					await content.DisposeAsync().ConfigureAwait(false);
				}
			}
		}
	}
}
