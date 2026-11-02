using System.Globalization;

namespace Secco.SDK.Documents.Internal;

/// <summary>Coluna já sem o tipo da linha: o que os gravadores precisam saber.</summary>
internal sealed record TableColumn(string Header, ExportColumnKind Kind, double Width);

/// <summary>Tudo que os gravadores precisam, além das linhas.</summary>
internal sealed record TableDocument(
	string Title,
	string? TenantName,
	IReadOnlyList<ExportFilter> Filters,
	string? IssuedBy,
	DateTimeOffset IssuedAt,
	TimeZoneInfo TimeZone,
	CultureInfo Culture,
	IReadOnlyList<TableColumn> Columns)
{
	/// <summary>Instante no fuso do pedido.</summary>
	internal DateTime Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, TimeZone).DateTime;

	/// <summary>Texto da célula como o PDF e o cabeçalho o exibem.</summary>
	internal string Format(ExportColumnKind kind, object? value) => value switch
	{
		null => string.Empty,
		string text => TextSanitizer.Clean(text),
		long number when kind == ExportColumnKind.WholeNumber => number.ToString("N0", Culture),
		decimal number when kind == ExportColumnKind.Currency => number.ToString("C", Culture),
		decimal number => number.ToString("N2", Culture),
		DateOnly date => date.ToString(Culture.DateTimeFormat.ShortDatePattern, Culture),
		DateTimeOffset instant => Local(instant).ToString(DateTimePattern, Culture),
		_ => TextSanitizer.Clean(Convert.ToString(value, Culture) ?? string.Empty),
	};

	internal string DateTimePattern => Culture.DateTimeFormat.ShortDatePattern + " " + Culture.DateTimeFormat.LongTimePattern;
}
