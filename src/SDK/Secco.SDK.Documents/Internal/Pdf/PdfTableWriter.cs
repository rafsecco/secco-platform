using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Secco.SDK.Documents.Internal.Pdf;

/// <summary>
/// PDF tabular gravado página a página (ADR-0058), com memória constante: cada página vai para o
/// arquivo assim que enche, e o "de Y" do rodapé é um objeto gravado depois da última. Fontes padrão
/// (Helvetica), sem embutir nada. O dado só entra no PDF como texto literal escapado; o gravador não
/// tem ação, link nem JavaScript.
/// </summary>
internal static class PdfTableWriter
{
	// A4 paisagem, em pontos.
	private const double PageWidth = 841.89;
	private const double PageHeight = 595.28;
	private const double Margin = 36;
	private const double FooterSpace = 22;

	private const double TitleSize = 13;
	private const double ContinuationSize = 9;
	private const double MetaSize = 8;
	private const double CellSize = 8;
	private const double FooterSize = 7;
	private const double LineGap = 2;
	private const double Padding = 3;

	private const int MaxCellLines = 4;
	private const int MaxHeaderLines = 2;
	private const int MaxFilterLines = 3;
	private const int MaxFilterValueLength = 200;

	private const string Ellipsis = "…";

	// Objetos reservados; páginas e conteúdos vêm depois.
	private const int CatalogObject = 1;
	private const int PagesObject = 2;
	private const int RegularFontObject = 3;
	private const int BoldFontObject = 4;
	private const int TotalPagesObject = 5;
	private const int InfoObject = 6;
	private const int FirstFreeObject = 7;

	/// <summary>Grava o PDF; devolve as linhas gravadas, ou -1 se passou de <paramref name="maxRows"/>.</summary>
	internal static async Task<int> WriteAsync(
		Stream output, TableDocument document, IAsyncEnumerable<object?[]> rows, int maxRows, CancellationToken cancellationToken)
	{
		var file = new PdfFile(output, FirstFreeObject);
		var layout = new Layout(document);
		var pageObjects = new List<int>();
		var footer = Footer(document);

		var page = new PageContent();
		var y = layout.FirstPageHeader(page);
		y = layout.TableHeader(page, y);
		var count = 0;

		await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
		{
			if (++count > maxRows)
			{
				return -1;
			}

			var cells = layout.WrapRow(row);
			var height = Layout.RowHeight(cells, CellSize);

			if (y - height < Margin + FooterSpace)
			{
				FinishPage(file, page, pageObjects, footer);
				page = new PageContent();
				y = layout.TableHeader(page, layout.ContinuationHeader(page));
			}

			y = layout.Row(page, y, cells, height);
		}

		if (count == 0)
		{
			page.Text(Margin + Padding, y - Padding - CellSize, CellSize, bold: false, "Nenhum registro.");
		}

		FinishPage(file, page, pageObjects, footer);
		WriteDocumentObjects(file, document, pageObjects);

		return count;
	}

	private static string Footer(TableDocument document)
	{
		var issuedAt = document.IssuedAt.DateTime.ToString(document.DateTimePattern, document.Culture);
		var who = string.IsNullOrWhiteSpace(document.IssuedBy) ? string.Empty : $"por {Layout.SingleLine(document.IssuedBy)} ";
		return $"Emitido {who}em {issuedAt} ({document.TimeZone.Id})";
	}

	private static void FinishPage(PdfFile file, PageContent page, List<int> pageObjects, string footer)
	{
		var number = pageObjects.Count + 1;

		// Rodapé: quem emitiu à esquerda; "Página N de " e o total (objeto reservado) à direita.
		page.Text(Margin, Margin, FooterSize, bold: false, footer);
		var label = $"Página {number.ToString(CultureInfo.InvariantCulture)} de ";
		var reserved = WinAnsi.Measure("00000", FooterSize, bold: false);
		var labelX = PageWidth - Margin - reserved - WinAnsi.Measure(label, FooterSize, bold: false);
		page.Text(labelX, Margin, FooterSize, bold: false, label);
		page.Raw($"q 1 0 0 1 {Num(labelX + WinAnsi.Measure(label, FooterSize, bold: false))} {Num(Margin)} cm /Tot Do Q\n");

		var contentObject = file.Allocate();
		file.WriteStream(contentObject, string.Empty, page.ToBytes());

		var pageObject = file.Allocate();
		file.WriteObject(pageObject, $"<< /Type /Page /Parent {PagesObject} 0 R /MediaBox [0 0 {Num(PageWidth)} {Num(PageHeight)}] " +
			$"/Resources << /Font << /F1 {RegularFontObject} 0 R /F2 {BoldFontObject} 0 R >> /XObject << /Tot {TotalPagesObject} 0 R >> >> " +
			$"/Contents {contentObject} 0 R >>");

		pageObjects.Add(pageObject);
	}

	private static void WriteDocumentObjects(PdfFile file, TableDocument document, List<int> pageObjects)
	{
		file.WriteObject(RegularFontObject, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
		file.WriteObject(BoldFontObject, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

		var total = Encoding.ASCII.GetBytes($"BT /F1 {Num(FooterSize)} Tf 0 0 Td {PdfText.Literal(pageObjects.Count.ToString(CultureInfo.InvariantCulture))} Tj ET");
		file.WriteStream(TotalPagesObject, $"/Type /XObject /Subtype /Form /BBox [0 -2 60 10] /Resources << /Font << /F1 {RegularFontObject} 0 R >> >>", total, compress: false);

		var kids = string.Join(' ', pageObjects.Select(n => $"{n} 0 R"));
		file.WriteObject(PagesObject, $"<< /Type /Pages /Kids [{kids}] /Count {pageObjects.Count} >>");
		file.WriteObject(InfoObject, $"<< /Producer (Secco.SDK.Documents) /Title {PdfText.Literal(Layout.SingleLine(document.Title))} >>");
		file.WriteObject(CatalogObject, $"<< /Type /Catalog /Pages {PagesObject} 0 R >>");
		file.Finish(CatalogObject, InfoObject);
	}

	internal static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

	/// <summary>Posições das colunas e o desenho de cabeçalhos e linhas.</summary>
	private sealed class Layout
	{
		private readonly TableDocument _document;
		private readonly double[] _x;
		private readonly double[] _width;

		internal Layout(TableDocument document)
		{
			_document = document;

			var available = PageWidth - (2 * Margin);
			var weights = document.Columns.Sum(c => c.Width);
			_x = new double[document.Columns.Count];
			_width = new double[document.Columns.Count];

			var x = Margin;
			for (var i = 0; i < document.Columns.Count; i++)
			{
				_x[i] = x;
				_width[i] = available * document.Columns[i].Width / weights;
				x += _width[i];
			}
		}

		internal double FirstPageHeader(PageContent page)
		{
			var y = PageHeight - Margin - TitleSize;
			var full = PageWidth - (2 * Margin);

			foreach (var line in Wrap(SingleLine(_document.Title), full, TitleSize, bold: true, maxLines: 2))
			{
				page.Text(Margin, y, TitleSize, bold: true, line);
				y -= TitleSize + LineGap;
			}

			y -= LineGap;

			if (!string.IsNullOrWhiteSpace(_document.TenantName))
			{
				page.Text(Margin, y, MetaSize, bold: false, "Tenant: " + SingleLine(_document.TenantName));
				y -= MetaSize + LineGap;
			}

			if (_document.Filters.Count > 0)
			{
				var filters = "Filtros: " + string.Join("; ", _document.Filters.Select(f =>
					$"{SingleLine(f.Name)}: {Truncate(SingleLine(f.Value), MaxFilterValueLength)}"));

				foreach (var line in Wrap(filters, full, MetaSize, bold: false, MaxFilterLines))
				{
					page.Text(Margin, y, MetaSize, bold: false, line);
					y -= MetaSize + LineGap;
				}
			}

			return y - (2 * LineGap);
		}

		internal double ContinuationHeader(PageContent page)
		{
			var y = PageHeight - Margin - ContinuationSize;
			var title = Truncate(SingleLine(_document.Title), 120) + " (continuação)";
			page.Text(Margin, y, ContinuationSize, bold: true, title);
			return y - ContinuationSize - (2 * LineGap);
		}

		internal double TableHeader(PageContent page, double top)
		{
			var cells = _document.Columns
				.Select((c, i) => Wrap(SingleLine(c.Header), _width[i] - (2 * Padding), CellSize, bold: true, MaxHeaderLines))
				.ToArray();
			var height = RowHeight(cells, CellSize);

			page.Raw($"0.92 g {Num(Margin)} {Num(top - height)} {Num(PageWidth - (2 * Margin))} {Num(height)} re f 0 g\n");
			Draw(page, top, cells, bold: true);
			return top - height;
		}

		internal List<string>[] WrapRow(object?[] row)
		{
			var cells = new List<string>[_document.Columns.Count];

			for (var i = 0; i < cells.Length; i++)
			{
				var text = _document.Format(_document.Columns[i].Kind, row[i]);
				cells[i] = Wrap(Normalize(text), _width[i] - (2 * Padding), CellSize, bold: false, MaxCellLines);
			}

			return cells;
		}

		internal double Row(PageContent page, double top, List<string>[] cells, double height)
		{
			Draw(page, top, cells, bold: false);
			var bottom = top - height;
			page.Raw($"0.8 G 0.4 w {Num(Margin)} {Num(bottom)} m {Num(PageWidth - Margin)} {Num(bottom)} l S\n");
			return bottom;
		}

		internal static double RowHeight(List<string>[] cells, double size)
			=> (cells.Max(c => c.Count) * (size + LineGap)) - LineGap + (2 * Padding);

		private void Draw(PageContent page, double top, List<string>[] cells, bool bold)
		{
			for (var i = 0; i < cells.Length; i++)
			{
				var rightAligned = !bold && _document.Columns[i].Kind is not ExportColumnKind.Text;
				var baseline = top - Padding - (CellSize * 0.8);

				foreach (var line in cells[i])
				{
					var x = rightAligned
						? _x[i] + _width[i] - Padding - WinAnsi.Measure(line, CellSize, bold)
						: _x[i] + Padding;

					page.Text(x, baseline, CellSize, bold, line);
					baseline -= CellSize + LineGap;
				}
			}
		}

		/// <summary>Quebra por palavra; palavra maior que a coluna é partida; além do limite, reticências.</summary>
		internal static List<string> Wrap(string text, double maxWidth, double size, bool bold, int maxLines)
		{
			var lines = new List<string>();

			foreach (var paragraph in text.Split('\n'))
			{
				var line = string.Empty;

				foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
				{
					var candidate = line.Length == 0 ? word : line + " " + word;

					if (WinAnsi.Measure(candidate, size, bold) <= maxWidth)
					{
						line = candidate;
						continue;
					}

					if (line.Length > 0)
					{
						lines.Add(line);
					}

					line = word;

					while (WinAnsi.Measure(line, size, bold) > maxWidth && line.Length > 1)
					{
						var fit = 1;
						while (fit < line.Length && WinAnsi.Measure(line.AsSpan(0, fit + 1), size, bold) <= maxWidth)
						{
							fit++;
						}

						lines.Add(line[..fit]);
						line = line[fit..];
					}
				}

				lines.Add(line);

				if (lines.Count > maxLines)
				{
					break;
				}
			}

			if (lines.Count <= maxLines)
			{
				return lines;
			}

			var kept = lines.GetRange(0, maxLines);
			var last = kept[^1];

			while (last.Length > 0 && WinAnsi.Measure(last + Ellipsis, size, bold) > maxWidth)
			{
				last = last[..^1];
			}

			kept[^1] = last + Ellipsis;
			return kept;
		}

		/// <summary>Acento composto vira o caractere pronto; o que não existe no WinAnsi vira um <c>?</c> só.</summary>
		internal static string Normalize(string text)
		{
			var builder = new StringBuilder(text.Length);

			foreach (var rune in text.Replace("\r\n", "\n", StringComparison.Ordinal).Normalize(NormalizationForm.FormC).EnumerateRunes())
			{
				if (rune.Value is '\n')
				{
					builder.Append('\n');
				}
				else if (rune.Value is '\t' or '\r')
				{
					builder.Append(' ');
				}
				else if (rune.IsBmp && WinAnsi.Encode((char)rune.Value) != WinAnsi.Replacement)
				{
					builder.Append((char)rune.Value);
				}
				else
				{
					builder.Append('?');
				}
			}

			return builder.ToString();
		}

		internal static string SingleLine(string text) => Normalize(TextSanitizer.Clean(text)).Replace('\n', ' ');

		private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + Ellipsis;
	}

	/// <summary>Operadores de uma página, comprimidos ao fechar.</summary>
	private sealed class PageContent
	{
		private readonly StringBuilder _operators = new();

		internal void Text(double x, double y, double size, bool bold, string text)
			=> _operators.Append("BT /").Append(bold ? "F2 " : "F1 ").Append(Num(size)).Append(" Tf ")
				.Append(Num(x)).Append(' ').Append(Num(y)).Append(" Td ").Append(PdfText.Literal(text)).Append(" Tj ET\n");

		internal void Raw(string operators) => _operators.Append(operators);

		internal byte[] ToBytes() => Encoding.ASCII.GetBytes(_operators.ToString());
	}
}

/// <summary>Escape de texto no conteúdo do PDF: a única fronteira entre o dado e os operadores.</summary>
internal static class PdfText
{
	/// <summary>
	/// String literal <c>(...)</c> em WinAnsi. <c>\</c>, <c>(</c> e <c>)</c> são escapados, e todo byte
	/// fora do ASCII imprimível vira octal: o conteúdo da página fica ASCII puro, e nenhum dado
	/// fecha a string para virar operador.
	/// </summary>
	internal static string Literal(string text)
	{
		var builder = new StringBuilder(text.Length + 2).Append('(');

		foreach (var c in text)
		{
			var b = WinAnsi.Encode(c);

			if (b is (byte)'(' or (byte)')' or (byte)'\\')
			{
				builder.Append('\\').Append((char)b);
			}
			else if (b is < 0x20 or > 0x7E)
			{
				builder.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
			}
			else
			{
				builder.Append((char)b);
			}
		}

		return builder.Append(')').ToString();
	}
}

/// <summary>Objetos, deslocamentos e tabela xref do arquivo.</summary>
internal sealed class PdfFile
{
	private readonly Stream _output;
	private readonly Dictionary<int, long> _offsets = [];
	private long _position;
	private int _next;

	internal PdfFile(Stream output, int firstFreeObject)
	{
		_output = output;
		_next = firstFreeObject;

		// Comentário binário logo após a versão: sinaliza aos transportes que o arquivo é binário.
		WriteAscii("%PDF-1.4\n");
		Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
	}

	internal int Allocate() => _next++;

	internal void WriteObject(int number, string body)
	{
		_offsets[number] = _position;
		WriteAscii($"{number} 0 obj\n{body}\nendobj\n");
	}

	internal void WriteStream(int number, string dictionary, byte[] content, bool compress = true)
	{
		var data = compress ? Deflate(content) : content;
		var filter = compress ? " /Filter /FlateDecode" : string.Empty;

		_offsets[number] = _position;
		WriteAscii($"{number} 0 obj\n<< {dictionary}{filter} /Length {data.Length} >>\nstream\n");
		Write(data);
		WriteAscii("\nendstream\nendobj\n");
	}

	internal void Finish(int catalog, int info)
	{
		var size = _next;
		var xref = _position;
		var table = new StringBuilder($"xref\n0 {size}\n0000000000 65535 f\r\n");

		for (var i = 1; i < size; i++)
		{
			var offset = _offsets.TryGetValue(i, out var value) ? value : 0;
			table.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n\r\n");
		}

		table.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {size} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
		WriteAscii(table.ToString());
	}

	private static byte[] Deflate(byte[] content)
	{
		using var buffer = new MemoryStream();
		using (var zlib = new ZLibStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
		{
			zlib.Write(content);
		}

		return buffer.ToArray();
	}

	private void WriteAscii(string text) => Write(Encoding.ASCII.GetBytes(text));

	private void Write(ReadOnlySpan<byte> bytes)
	{
		_output.Write(bytes);
		_position += bytes.Length;
	}
}
