using System.Globalization;
using System.Text;

namespace Secco.SDK.Documents.Internal;

/// <summary>
/// Nome do arquivo gerado no servidor (ADR-0058): prefixo do produto reduzido a <c>[a-z0-9-]</c>,
/// mais a data no fuso do pedido. Nada vindo do usuário chega ao <c>Content-Disposition</c>.
/// </summary>
internal static class ExportFileName
{
	private const int MaxPrefixLength = 40;
	private const string Fallback = "exportacao";

	internal static string Create(string prefix, ExportFormat format, DateTime issuedAtLocal)
	{
		var extension = format == ExportFormat.Pdf ? "pdf" : "xlsx";
		var stamp = issuedAtLocal.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
		return $"{Sanitize(prefix)}-{stamp}.{extension}";
	}

	internal static string Sanitize(string? prefix)
	{
		var builder = new StringBuilder(MaxPrefixLength);

		foreach (var c in (prefix ?? string.Empty).Normalize(NormalizationForm.FormD))
		{
			if (builder.Length == MaxPrefixLength)
			{
				break;
			}

			var lower = char.ToLowerInvariant(c);

			if (char.IsAsciiLetterLower(lower) || char.IsAsciiDigit(lower))
			{
				builder.Append(lower);
			}
			else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
				&& builder.Length > 0 && builder[^1] != '-')
			{
				// Acento some (ã → a); qualquer outro caractere vira um hífen só.
				builder.Append('-');
			}
		}

		var result = builder.ToString().Trim('-');
		return result.Length == 0 ? Fallback : result;
	}
}
