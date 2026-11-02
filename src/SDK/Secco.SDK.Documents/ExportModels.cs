using System.Collections;
using System.Globalization;

namespace Secco.SDK.Documents;

/// <summary>Formato do arquivo exportado (ADR-0058).</summary>
public enum ExportFormat
{
	/// <summary>Planilha Excel (.xlsx).</summary>
	Xlsx,

	/// <summary>PDF tabular no layout da plataforma.</summary>
	Pdf,
}

/// <summary>Tipo de uma coluna: define a célula do Excel e a formatação no PDF.</summary>
public enum ExportColumnKind
{
	/// <summary>Texto, sempre gravado como texto (nunca fórmula).</summary>
	Text,

	/// <summary>Número inteiro.</summary>
	WholeNumber,

	/// <summary>Número com duas casas decimais.</summary>
	Number,

	/// <summary>Valor monetário, no símbolo da cultura do pedido.</summary>
	Currency,

	/// <summary>Data sem hora.</summary>
	Date,

	/// <summary>Instante, convertido para o fuso do pedido.</summary>
	DateTime,
}

/// <summary>Uma coluna da exportação.</summary>
/// <typeparam name="T">Tipo da linha.</typeparam>
public sealed class ExportColumn<T>
{
	internal ExportColumn(string header, ExportColumnKind kind, double width, Func<T, object?> value)
	{
		Header = header;
		Kind = kind;
		Width = width;
		Value = value;
	}

	/// <summary>Título da coluna.</summary>
	public string Header { get; }

	/// <summary>Tipo da coluna.</summary>
	public ExportColumnKind Kind { get; }

	/// <summary>Largura em caracteres no Excel; no PDF, o peso relativo da coluna.</summary>
	public double Width { get; }

	internal Func<T, object?> Value { get; }
}

/// <summary>Definição das colunas, montada em sequência: <c>new ExportColumns&lt;T&gt;().Text(...).DateTime(...)</c>.</summary>
/// <typeparam name="T">Tipo da linha.</typeparam>
public sealed class ExportColumns<T> : IReadOnlyList<ExportColumn<T>>
{
	private readonly List<ExportColumn<T>> _columns = [];

	/// <inheritdoc />
	public int Count => _columns.Count;

	/// <inheritdoc />
	public ExportColumn<T> this[int index] => _columns[index];

	/// <summary>Coluna de texto.</summary>
	/// <param name="header">Título.</param>
	/// <param name="value">Valor da linha.</param>
	/// <param name="width">Largura em caracteres.</param>
	public ExportColumns<T> Text(string header, Func<T, string?> value, double width = 24)
		=> Add(header, ExportColumnKind.Text, width, value is null ? null : row => value(row));

	/// <summary>Coluna de número inteiro.</summary>
	/// <param name="header">Título.</param>
	/// <param name="value">Valor da linha.</param>
	/// <param name="width">Largura em caracteres.</param>
	public ExportColumns<T> WholeNumber(string header, Func<T, long?> value, double width = 10)
		=> Add(header, ExportColumnKind.WholeNumber, width, value is null ? null : row => value(row));

	/// <summary>Coluna de número decimal (duas casas).</summary>
	/// <param name="header">Título.</param>
	/// <param name="value">Valor da linha.</param>
	/// <param name="width">Largura em caracteres.</param>
	public ExportColumns<T> Number(string header, Func<T, decimal?> value, double width = 12)
		=> Add(header, ExportColumnKind.Number, width, value is null ? null : row => value(row));

	/// <summary>Coluna monetária.</summary>
	/// <param name="header">Título.</param>
	/// <param name="value">Valor da linha.</param>
	/// <param name="width">Largura em caracteres.</param>
	public ExportColumns<T> Currency(string header, Func<T, decimal?> value, double width = 14)
		=> Add(header, ExportColumnKind.Currency, width, value is null ? null : row => value(row));

	/// <summary>Coluna de data sem hora.</summary>
	/// <param name="header">Título.</param>
	/// <param name="value">Valor da linha.</param>
	/// <param name="width">Largura em caracteres.</param>
	public ExportColumns<T> Date(string header, Func<T, DateOnly?> value, double width = 12)
		=> Add(header, ExportColumnKind.Date, width, value is null ? null : row => value(row));

	/// <summary>Coluna de instante, exibida no fuso do pedido.</summary>
	/// <param name="header">Título.</param>
	/// <param name="value">Valor da linha.</param>
	/// <param name="width">Largura em caracteres.</param>
	public ExportColumns<T> DateTime(string header, Func<T, DateTimeOffset?> value, double width = 20)
		=> Add(header, ExportColumnKind.DateTime, width, value is null ? null : row => value(row));

	/// <inheritdoc />
	public IEnumerator<ExportColumn<T>> GetEnumerator() => _columns.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	private ExportColumns<T> Add(string header, ExportColumnKind kind, double width, Func<T, object?>? value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(header);
		ArgumentNullException.ThrowIfNull(value);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);

		_columns.Add(new ExportColumn<T>(header, kind, width, value));
		return this;
	}
}

/// <summary>Filtro aplicado, exibido no cabeçalho do PDF e na aba de informações do Excel.</summary>
/// <param name="Name">Nome do filtro.</param>
/// <param name="Value">Valor, como o usuário o vê.</param>
public sealed record ExportFilter(string Name, string Value);

/// <summary>Pedido de exportação.</summary>
/// <typeparam name="T">Tipo da linha.</typeparam>
/// <param name="Format">Formato do arquivo.</param>
/// <param name="Title">Título do documento.</param>
/// <param name="FileNamePrefix">Prefixo fixo do nome do arquivo (a SDK acrescenta data e extensão).</param>
/// <param name="Columns">Colunas, ao menos uma.</param>
/// <param name="Rows">Linhas, consumidas uma a uma.</param>
public sealed record ExportRequest<T>(
	ExportFormat Format,
	string Title,
	string FileNamePrefix,
	ExportColumns<T> Columns,
	IAsyncEnumerable<T> Rows)
{
	/// <summary>Nome do tenant, para o cabeçalho.</summary>
	public string? TenantName { get; init; }

	/// <summary>Filtros aplicados, para o cabeçalho.</summary>
	public IReadOnlyList<ExportFilter> Filters { get; init; } = [];

	/// <summary>Quem emitiu, para o rodapé.</summary>
	public string? IssuedBy { get; init; }

	/// <summary>Fuso das datas e do "emitido em" (padrão: UTC).</summary>
	public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

	/// <summary>Cultura de formatação no PDF (padrão: pt-BR).</summary>
	public CultureInfo Culture { get; init; } = CultureInfo.GetCultureInfo("pt-BR");
}

/// <summary>
/// Documento gerado, pronto para envio. Fica num arquivo temporário apagado ao descartar:
/// quem recebe é responsável por descartar (o helper HTTP descarta ao terminar o envio).
/// </summary>
public sealed class ExportedDocument : IAsyncDisposable, IDisposable
{
	internal ExportedDocument(Stream content, ExportFormat format, string fileName, int rowCount)
	{
		Content = content;
		Format = format;
		FileName = fileName;
		RowCount = rowCount;
	}

	/// <summary>Conteúdo, posicionado no início.</summary>
	public Stream Content { get; }

	/// <summary>Formato.</summary>
	public ExportFormat Format { get; }

	/// <summary>Nome do arquivo, gerado no servidor.</summary>
	public string FileName { get; }

	/// <summary>Quantas linhas saíram — o que vai para a trilha de auditoria.</summary>
	public int RowCount { get; }

	/// <summary>Tamanho em bytes.</summary>
	public long Length => Content.Length;

	/// <summary>Tipo de mídia do formato.</summary>
	public string ContentType => SeccoDocumentResults.ContentTypeOf(Format);

	/// <inheritdoc />
	public ValueTask DisposeAsync()
	{
		GC.SuppressFinalize(this);
		return Content.DisposeAsync();
	}

	/// <inheritdoc />
	public void Dispose()
	{
		GC.SuppressFinalize(this);
		Content.Dispose();
	}
}
