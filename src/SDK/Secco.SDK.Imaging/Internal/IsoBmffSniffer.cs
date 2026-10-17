using System.Text;

namespace Secco.SDK.Imaging.Internal;

/// <summary>
/// Reconhece contêineres ISO-BMFF de imagem (HEIC/HEIF/AVIF) pela caixa <c>ftyp</c>. O Skia não os
/// decodifica nesta plataforma; reconhecê-los dá uma resposta honesta (formato não suportado)
/// em vez de "imagem inválida" para um arquivo de iPhone legítimo.
/// </summary>
internal static class IsoBmffSniffer
{
	private static readonly string[] Brands = ["heic", "heix", "mif1", "msf1", "avif"];

	internal static bool IsUnsupportedImageContainer(ReadOnlySpan<byte> data)
	{
		if (data.Length < 12 || !data.Slice(4, 4).SequenceEqual("ftyp"u8))
		{
			return false;
		}

		var brand = Encoding.ASCII.GetString(data.Slice(8, 4));
		return Array.Exists(Brands, known => string.Equals(known, brand, StringComparison.Ordinal));
	}
}
