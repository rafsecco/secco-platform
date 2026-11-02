using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Secco.SDK.Documents.Internal;

/// <summary>
/// Planilha gravada linha a linha (ADR-0058): um <c>.xlsx</c> é um zip com XMLs fixos, e a aba de
/// dados sai direto pelo <see cref="XmlWriter"/>, com memória constante. Todo texto é
/// <c>inlineStr</c> — nunca fórmula, então <c>=</c>, <c>+</c>, <c>-</c> e <c>@</c> continuam texto.
/// </summary>
internal static class XlsxTableWriter
{
	private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
	private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

	// Índices de cellXfs em styles.xml.
	private const int HeaderStyle = 1;
	private const int IntegerStyle = 2;
	private const int DecimalStyle = 3;
	private const int CurrencyStyle = 4;
	private const int DateStyle = 5;
	private const int DateTimeStyle = 6;

	// O Excel não representa data antes de 1900 como número: sai como texto.
	private static readonly DateTime FirstSerialDate = new(1900, 3, 1);

	private static readonly XmlWriterSettings Settings = new()
	{
		Encoding = new UTF8Encoding(false),
		CloseOutput = false,
	};

	/// <summary>Grava a planilha; devolve as linhas gravadas, ou -1 se passou de <paramref name="maxRows"/>.</summary>
	internal static async Task<int> WriteAsync(
		Stream output, TableDocument document, IAsyncEnumerable<object?[]> rows, int maxRows, CancellationToken cancellationToken)
	{
		using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

		Part(zip, "[Content_Types].xml", ContentTypes);
		Part(zip, "_rels/.rels", RootRelationships);
		Part(zip, "xl/workbook.xml", Workbook);
		Part(zip, "xl/_rels/workbook.xml.rels", WorkbookRelationships);
		Part(zip, "xl/styles.xml", Styles(document.Culture));
		WriteAbout(zip, document);

		await using var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest).Open();

		// Síncrono de propósito: escreve num arquivo local, e o XmlWriter assíncrono recusaria Dispose síncrono.
		using var xml = XmlWriter.Create(entry, Settings);

		var columns = document.Columns;
		xml.WriteStartDocument(true);
		xml.WriteStartElement("worksheet", Main);

		// Cabeçalho congelado: rola os dados, o título das colunas fica.
		xml.WriteStartElement("sheetViews", Main);
		xml.WriteStartElement("sheetView", Main);
		xml.WriteAttributeString("workbookViewId", "0");
		xml.WriteStartElement("pane", Main);
		xml.WriteAttributeString("ySplit", "1");
		xml.WriteAttributeString("topLeftCell", "A2");
		xml.WriteAttributeString("activePane", "bottomLeft");
		xml.WriteAttributeString("state", "frozen");
		xml.WriteEndElement();
		xml.WriteEndElement();
		xml.WriteEndElement();

		xml.WriteStartElement("cols", Main);
		for (var i = 0; i < columns.Count; i++)
		{
			var position = (i + 1).ToString(CultureInfo.InvariantCulture);
			xml.WriteStartElement("col", Main);
			xml.WriteAttributeString("min", position);
			xml.WriteAttributeString("max", position);
			xml.WriteAttributeString("width", columns[i].Width.ToString("0.##", CultureInfo.InvariantCulture));
			xml.WriteAttributeString("customWidth", "1");
			xml.WriteEndElement();
		}

		xml.WriteEndElement();

		xml.WriteStartElement("sheetData", Main);
		xml.WriteStartElement("row", Main);
		foreach (var column in columns)
		{
			TextCell(xml, column.Header, HeaderStyle);
		}

		xml.WriteEndElement();

		var count = 0;

		await foreach (var row in rows.WithCancellation(cancellationToken).ConfigureAwait(false))
		{
			if (++count > maxRows)
			{
				return -1;
			}

			xml.WriteStartElement("row", Main);
			for (var i = 0; i < columns.Count; i++)
			{
				Cell(xml, document, columns[i].Kind, row[i]);
			}

			xml.WriteEndElement();
		}

		xml.WriteEndElement();

		xml.WriteStartElement("autoFilter", Main);
		xml.WriteAttributeString("ref", $"A1:{ColumnName(columns.Count)}{(count + 1).ToString(CultureInfo.InvariantCulture)}");
		xml.WriteEndElement();

		xml.WriteEndElement();
		xml.WriteEndDocument();

		return count;
	}

	private static void Cell(XmlWriter xml, TableDocument document, ExportColumnKind kind, object? value)
	{
		switch (value)
		{
			case null:
				xml.WriteStartElement("c", Main);
				xml.WriteEndElement();
				break;
			case string text:
				TextCell(xml, text, style: 0);
				break;
			case long number:
				NumberCell(xml, number.ToString(CultureInfo.InvariantCulture), IntegerStyle);
				break;
			case decimal number:
				NumberCell(xml, number.ToString(CultureInfo.InvariantCulture), kind == ExportColumnKind.Currency ? CurrencyStyle : DecimalStyle);
				break;
			case DateOnly date:
				DateCell(xml, document, date.ToDateTime(TimeOnly.MinValue), DateStyle);
				break;
			case DateTimeOffset instant:
				DateCell(xml, document, document.Local(instant), DateTimeStyle);
				break;
			default:
				TextCell(xml, document.Format(kind, value), style: 0);
				break;
		}
	}

	private static void DateCell(XmlWriter xml, TableDocument document, DateTime value, int style)
	{
		if (value < FirstSerialDate)
		{
			TextCell(xml, value.ToString(style == DateStyle ? document.Culture.DateTimeFormat.ShortDatePattern : document.DateTimePattern, document.Culture), style: 0);
			return;
		}

		NumberCell(xml, value.ToOADate().ToString("R", CultureInfo.InvariantCulture), style);
	}

	private static void NumberCell(XmlWriter xml, string invariantValue, int style)
	{
		xml.WriteStartElement("c", Main);
		xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
		xml.WriteElementString("v", Main, invariantValue);
		xml.WriteEndElement();
	}

	private static void TextCell(XmlWriter xml, string text, int style)
	{
		var value = TextSanitizer.ForExcelCell(text);

		xml.WriteStartElement("c", Main);
		xml.WriteAttributeString("t", "inlineStr");

		if (style != 0)
		{
			xml.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
		}

		xml.WriteStartElement("is", Main);
		xml.WriteStartElement("t", Main);

		if (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
		{
			xml.WriteAttributeString("xml", "space", null, "preserve");
		}

		xml.WriteString(value);
		xml.WriteEndElement();
		xml.WriteEndElement();
		xml.WriteEndElement();
	}

	/// <summary>Aba "Sobre": título, tenant, filtros e emissão, sem misturar com os dados.</summary>
	private static void WriteAbout(ZipArchive zip, TableDocument document)
	{
		using var entry = zip.CreateEntry("xl/worksheets/sheet2.xml", CompressionLevel.Fastest).Open();
		using var xml = XmlWriter.Create(entry, Settings);

		xml.WriteStartDocument(true);
		xml.WriteStartElement("worksheet", Main);
		xml.WriteStartElement("cols", Main);
		xml.WriteStartElement("col", Main);
		xml.WriteAttributeString("min", "1");
		xml.WriteAttributeString("max", "1");
		xml.WriteAttributeString("width", "24");
		xml.WriteAttributeString("customWidth", "1");
		xml.WriteEndElement();
		xml.WriteStartElement("col", Main);
		xml.WriteAttributeString("min", "2");
		xml.WriteAttributeString("max", "2");
		xml.WriteAttributeString("width", "80");
		xml.WriteAttributeString("customWidth", "1");
		xml.WriteEndElement();
		xml.WriteEndElement();
		xml.WriteStartElement("sheetData", Main);

		void Line(string name, string value)
		{
			xml.WriteStartElement("row", Main);
			TextCell(xml, name, HeaderStyle);
			TextCell(xml, value, style: 0);
			xml.WriteEndElement();
		}

		Line("Título", document.Title);

		if (!string.IsNullOrWhiteSpace(document.TenantName))
		{
			Line("Tenant", document.TenantName);
		}

		foreach (var filter in document.Filters)
		{
			Line(filter.Name, filter.Value);
		}

		if (!string.IsNullOrWhiteSpace(document.IssuedBy))
		{
			Line("Emitido por", document.IssuedBy);
		}

		Line("Emitido em", document.IssuedAt.DateTime.ToString(document.DateTimePattern, document.Culture) + " (" + document.TimeZone.Id + ")");

		xml.WriteEndElement();
		xml.WriteEndElement();
		xml.WriteEndDocument();
	}

	private static string ColumnName(int position)
	{
		var name = string.Empty;

		while (position > 0)
		{
			var remainder = (position - 1) % 26;
			name = (char)('A' + remainder) + name;
			position = (position - 1) / 26;
		}

		return name;
	}

	private static void Part(ZipArchive zip, string name, string content)
	{
		using var stream = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
		var bytes = Encoding.UTF8.GetBytes(content);
		stream.Write(bytes);
	}

	private static string Styles(CultureInfo culture)
	{
		// Código de formato do Excel: o símbolo entra entre aspas, com aspas internas dobradas.
		var symbol = culture.NumberFormat.CurrencySymbol.Replace("\"", "\"\"", StringComparison.Ordinal);
		var currency = System.Security.SecurityElement.Escape($"\"{symbol}\" #,##0.00");

		return $"""
			<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
			<styleSheet xmlns="{Main}">
			<numFmts count="3"><numFmt numFmtId="164" formatCode="dd/mm/yyyy"/><numFmt numFmtId="165" formatCode="dd/mm/yyyy hh:mm:ss"/><numFmt numFmtId="166" formatCode="{currency}"/></numFmts>
			<fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
			<fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
			<borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
			<cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
			<cellXfs count="7"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="3" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="4" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="166" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/><xf numFmtId="165" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs>
			<cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
			</styleSheet>
			""";
	}

	private const string ContentTypes = """
		<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
		<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
		""";

	private const string RootRelationships = """
		<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
		<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
		""";

	private const string Workbook = $"""
		<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
		<workbook xmlns="{Main}" xmlns:r="{Relationships}"><sheets><sheet name="Dados" sheetId="1" r:id="rId1"/><sheet name="Sobre" sheetId="2" r:id="rId2"/></sheets></workbook>
		""";

	private const string WorkbookRelationships = """
		<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
		<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
		""";
}
