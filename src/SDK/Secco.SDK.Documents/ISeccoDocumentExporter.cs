using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Secco.SDK.Documents.Internal;
using Secco.SDK.Documents.Internal.Pdf;
using Secco.SharedKernel.Results;

namespace Secco.SDK.Documents;

/// <summary>
/// Exporta uma listagem em Excel ou PDF (ADR-0058). Só o formato: buscar os dados, aplicar tenant e
/// autorização, e registrar a trilha de auditoria ficam com o produto.
/// </summary>
public interface ISeccoDocumentExporter
{
	/// <summary>
	/// Gera o documento num arquivo temporário e o devolve pronto para envio. Gera primeiro, envia
	/// depois: falha no meio nunca vira arquivo truncado com status 200.
	/// </summary>
	/// <typeparam name="T">Tipo da linha.</typeparam>
	/// <param name="request">Colunas, linhas e o que vai no cabeçalho.</param>
	/// <param name="cancellationToken">Cancela a espera por vaga e a geração (use o <c>RequestAborted</c>).</param>
	/// <returns>
	/// O documento — quem recebe o descarta, e o helper HTTP descarta ao terminar o envio — ou
	/// <see cref="SeccoDocumentsErrors.TooManyRows"/>.
	/// </returns>
	/// <exception cref="ArgumentException">O pedido é inválido (bug do chamador).</exception>
	Task<Result<ExportedDocument>> ExportAsync<T>(ExportRequest<T> request, CancellationToken cancellationToken = default);
}

internal sealed class SeccoDocumentExporter : ISeccoDocumentExporter, IDisposable
{
	private readonly SeccoDocumentsOptions _options;
	private readonly TimeProvider _time;
	private readonly TempFileStore _store;
	private readonly SemaphoreSlim _slots;

	internal SeccoDocumentExporter(IOptions<SeccoDocumentsOptions> options, TimeProvider time)
	{
		_options = options.Value;
		_time = time;
		_store = new TempFileStore(_options.TempDirectory, time);
		_slots = new SemaphoreSlim(_options.MaxConcurrentExports, _options.MaxConcurrentExports);
	}

	internal TempFileStore Store => _store;

	public async Task<Result<ExportedDocument>> ExportAsync<T>(ExportRequest<T> request, CancellationToken cancellationToken = default)
	{
		ThrowIfInvalid(request);

		var issuedAt = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), request.TimeZone);
		var document = new TableDocument(
			request.Title,
			request.TenantName,
			request.Filters,
			request.IssuedBy,
			issuedAt,
			request.TimeZone,
			request.Culture,
			[.. request.Columns.Select(c => new TableColumn(c.Header, c.Kind, c.Width))]);
		var maxRows = request.Format == ExportFormat.Pdf ? _options.MaxPdfRows : _options.MaxXlsxRows;

		await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
		FileStream? file = null;

		try
		{
			file = _store.Create();
			var rows = Project(request, cancellationToken);
			var written = request.Format == ExportFormat.Pdf
				? await PdfTableWriter.WriteAsync(file, document, rows, maxRows, cancellationToken).ConfigureAwait(false)
				: await XlsxTableWriter.WriteAsync(file, document, rows, maxRows, cancellationToken).ConfigureAwait(false);

			if (written < 0)
			{
				return SeccoDocumentsErrors.TooManyRows;
			}

			await file.FlushAsync(cancellationToken).ConfigureAwait(false);
			file.Position = 0;

			var exported = new ExportedDocument(file, request.Format, ExportFileName.Create(request.FileNamePrefix, request.Format, issuedAt.DateTime), written);
			file = null;
			return exported;
		}
		finally
		{
			// Sem sucesso, o arquivo some aqui (DeleteOnClose); com sucesso, some quando o documento é descartado.
			if (file is not null)
			{
				await file.DisposeAsync().ConfigureAwait(false);
			}

			_slots.Release();
		}
	}

	public void Dispose() => _slots.Dispose();

	private static async IAsyncEnumerable<object?[]> Project<T>(ExportRequest<T> request, [EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var columns = request.Columns;

		await foreach (var row in request.Rows.WithCancellation(cancellationToken).ConfigureAwait(false))
		{
			var values = new object?[columns.Count];

			for (var i = 0; i < values.Length; i++)
			{
				values[i] = columns[i].Value(row);
			}

			yield return values;
		}
	}

	private static void ThrowIfInvalid<T>(ExportRequest<T> request)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (!Enum.IsDefined(request.Format))
		{
			throw new ArgumentException("Formato de exportação desconhecido.", nameof(request));
		}

		if (string.IsNullOrWhiteSpace(request.Title))
		{
			throw new ArgumentException("Informe o título do documento.", nameof(request));
		}

		if (request.Columns is null || request.Columns.Count == 0)
		{
			throw new ArgumentException("Informe ao menos uma coluna.", nameof(request));
		}

		if (request.Rows is null || request.Filters is null || request.TimeZone is null || request.Culture is null)
		{
			throw new ArgumentException("Linhas, filtros, fuso e cultura são obrigatórios.", nameof(request));
		}
	}
}
