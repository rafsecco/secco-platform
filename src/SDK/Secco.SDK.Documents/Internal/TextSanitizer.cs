using System.Text;

namespace Secco.SDK.Documents.Internal;

/// <summary>
/// Limpa o dado antes de qualquer gravador (ADR-0058): um caractere de controle colado num campo
/// derrubaria a exportação inteira, porque o XML não o aceita. Limpar, e não lançar.
/// </summary>
internal static class TextSanitizer
{
	/// <summary>Limite de caracteres de uma célula do Excel.</summary>
	internal const int ExcelCellLimit = 32_767;

	private const string TruncationMarker = "…";

	/// <summary>Remove o que não é caractere válido de XML 1.0 (controle, surrogate solto, U+FFFE/FFFF).</summary>
	internal static string Clean(string value)
	{
		if (IsClean(value))
		{
			return value;
		}

		var builder = new StringBuilder(value.Length);

		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];

			if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
			{
				builder.Append(c).Append(value[++i]);
			}
			else if (IsValidSingle(c))
			{
				builder.Append(c);
			}
		}

		return builder.ToString();
	}

	/// <summary>Limpa e corta no limite da célula do Excel, com marcador, sem partir um par de surrogates.</summary>
	internal static string ForExcelCell(string value)
	{
		var clean = Clean(value);

		if (clean.Length <= ExcelCellLimit)
		{
			return clean;
		}

		var cut = ExcelCellLimit - TruncationMarker.Length;

		if (char.IsHighSurrogate(clean[cut - 1]))
		{
			cut--;
		}

		return string.Concat(clean.AsSpan(0, cut), TruncationMarker);
	}

	private static bool IsClean(string value)
	{
		for (var i = 0; i < value.Length; i++)
		{
			var c = value[i];

			if (char.IsSurrogate(c))
			{
				if (!char.IsHighSurrogate(c) || i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
				{
					return false;
				}

				i++;
			}
			else if (!IsValidSingle(c))
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsValidSingle(char c)
		=> c is '\t' or '\n' or '\r' || (c >= ' ' && !char.IsSurrogate(c) && c is not '￾' and not '￿');
}
