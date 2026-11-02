using System.Collections.Frozen;

namespace Secco.SDK.Documents.Internal.Pdf;

/// <summary>
/// Codificação <c>WinAnsiEncoding</c> das fontes padrão do PDF e as larguras da Helvetica
/// (ADR-0058). Cobre todo o português; o que fica fora (emoji, CJK) sai como <c>?</c>.
/// As larguras vêm da Arial, desenhada com as mesmas medidas da Helvetica, em milésimos de em.
/// </summary>
internal static class WinAnsi
{
	internal const byte Replacement = (byte)'?';

	// 0x80–0x9F do WinAnsi não são os mesmos pontos do Unicode.
	private static readonly FrozenDictionary<char, byte> HighRange = new Dictionary<char, byte>
	{
		['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85,
		['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A,
		['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E, ['‘'] = 0x91, ['’'] = 0x92,
		['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
		['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C,
		['ž'] = 0x9E, ['Ÿ'] = 0x9F,
	}.ToFrozenDictionary();

	// Larguras de 0x20 a 0xFF (224 valores); 0 = posição indefinida no WinAnsi.
	private static readonly short[] RegularWidths =
	[
		278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556,
		556, 556, 278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
		667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556,
		556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 0,
		556, 0, 222, 556, 333, 1000, 556, 556, 333, 1000, 667, 333, 1000, 0, 611, 0, 0, 222, 222, 333, 333, 350, 556, 1000,
		333, 1000, 500, 333, 944, 0, 500, 667, 278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 552,
		400, 549, 333, 333, 333, 576, 537, 333, 333, 333, 365, 556, 834, 834, 834, 611, 667, 667, 667, 667, 667, 667, 1000, 722,
		667, 667, 667, 667, 278, 278, 278, 278, 722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
		556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278, 556, 556, 556, 556, 556, 556, 556, 549,
		611, 556, 556, 556, 556, 500, 556, 500,
	];

	private static readonly short[] BoldWidths =
	[
		278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556,
		556, 556, 333, 333, 584, 584, 584, 611, 975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
		667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556, 333, 556, 611, 556, 611, 556, 333, 611,
		611, 278, 278, 556, 278, 889, 611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584, 0,
		556, 0, 278, 556, 500, 1000, 556, 556, 333, 1000, 667, 333, 1000, 0, 611, 0, 0, 278, 278, 500, 500, 350, 556, 1000,
		333, 1000, 556, 333, 944, 0, 500, 667, 278, 333, 556, 556, 556, 556, 280, 556, 333, 737, 370, 556, 584, 333, 737, 552,
		400, 549, 333, 333, 333, 576, 556, 333, 333, 333, 365, 556, 834, 834, 834, 611, 722, 722, 722, 722, 722, 722, 1000, 722,
		667, 667, 667, 667, 278, 278, 278, 278, 722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
		556, 556, 556, 556, 556, 556, 889, 556, 556, 556, 556, 556, 278, 278, 278, 278, 611, 611, 611, 611, 611, 611, 611, 549,
		611, 611, 611, 611, 611, 556, 611, 556,
	];

	/// <summary>Byte WinAnsi do caractere, ou <see cref="Replacement"/> se não existe na codificação.</summary>
	internal static byte Encode(char c)
	{
		if (c is >= ' ' and <= '~' || c is >= ' ' and <= 'ÿ')
		{
			return (byte)c;
		}

		return HighRange.TryGetValue(c, out var mapped) ? mapped : Replacement;
	}

	/// <summary>Largura do byte em milésimos de em.</summary>
	internal static int Width(byte value, bool bold)
	{
		if (value < 0x20)
		{
			return 0;
		}

		var width = (bold ? BoldWidths : RegularWidths)[value - 0x20];
		return width == 0 ? (bold ? BoldWidths : RegularWidths)[Replacement - 0x20] : width;
	}

	/// <summary>Largura do texto em pontos, no corpo informado.</summary>
	internal static double Measure(ReadOnlySpan<char> text, double fontSize, bool bold)
	{
		var total = 0;

		foreach (var c in text)
		{
			total += Width(Encode(c), bold);
		}

		return total * fontSize / 1000d;
	}
}
