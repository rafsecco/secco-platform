using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Secco.SDK.Imaging.Tests;

/// <summary>Geradores de imagens de teste: nada de binário versionado, tudo montado em código.</summary>
internal static class TestImages
{
	/// <summary>Metade esquerda vermelha, direita azul.</summary>
	internal static SKBitmap Bitmap(int width, int height)
	{
		var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var canvas = new SKCanvas(bitmap);
		using var red = new SKPaint { Color = SKColors.Red };
		using var blue = new SKPaint { Color = SKColors.Blue };
		canvas.DrawRect(0, 0, width / 2f, height, red);
		canvas.DrawRect(width / 2f, 0, width - (width / 2f), height, blue);
		return bitmap;
	}

	/// <summary>Quatro quadrantes: superior esquerdo vermelho, superior direito verde, inferior esquerdo azul, inferior direito amarelo.</summary>
	internal static byte[] QuadrantsJpeg(int width, int height)
	{
		using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var canvas = new SKCanvas(bitmap);
		var (halfW, halfH) = (width / 2f, height / 2f);
		using var red = new SKPaint { Color = SKColors.Red };
		using var green = new SKPaint { Color = new SKColor(0, 255, 0) };
		using var blue = new SKPaint { Color = SKColors.Blue };
		using var yellow = new SKPaint { Color = SKColors.Yellow };
		canvas.DrawRect(0, 0, halfW, halfH, red);
		canvas.DrawRect(halfW, 0, width - halfW, halfH, green);
		canvas.DrawRect(0, halfH, halfW, height - halfH, blue);
		canvas.DrawRect(halfW, halfH, width - halfW, height - halfH, yellow);
		return Encode(bitmap, SKEncodedImageFormat.Jpeg, 100);
	}

	internal static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 90)
	{
		using var image = SKImage.FromBitmap(bitmap);
		using var data = image.Encode(format, quality);
		return data.ToArray();
	}

	internal static byte[] Png(int width, int height)
	{
		using var bitmap = Bitmap(width, height);
		return Encode(bitmap, SKEncodedImageFormat.Png);
	}

	internal static byte[] Jpeg(int width, int height)
	{
		using var bitmap = Bitmap(width, height);
		return Encode(bitmap, SKEncodedImageFormat.Jpeg);
	}

	internal static byte[] WebP(int width, int height)
	{
		using var bitmap = Bitmap(width, height);
		return Encode(bitmap, SKEncodedImageFormat.Webp);
	}

	/// <summary>JPEG com um APP1/EXIF montado à mão logo após o SOI (Orientation e ImageDescription).</summary>
	internal static byte[] JpegWithExif(int width, int height, ushort orientation, string marker, bool quadrants = false)
	{
		var jpeg = quadrants ? QuadrantsJpeg(width, height) : Jpeg(width, height);
		var description = Encoding.ASCII.GetBytes(marker + "\0");

		// TIFF little-endian: cabeçalho (8) + IFD0 (2 + 2*12 + 4 = 30) + dados da descrição.
		var tiff = new List<byte>();
		tiff.AddRange("II"u8.ToArray());
		tiff.AddRange(U16(42));
		tiff.AddRange(U32(8));
		tiff.AddRange(U16(2));
		tiff.AddRange(Entry(0x010E, 2, (uint)description.Length, 38));
		tiff.AddRange(Entry(0x0112, 3, 1, orientation));
		tiff.AddRange(U32(0));
		tiff.AddRange(description);

		var payload = new List<byte>();
		payload.AddRange("Exif\0\0"u8.ToArray());
		payload.AddRange(tiff);

		var app1 = new List<byte> { 0xFF, 0xE1 };
		app1.AddRange(BigEndian((ushort)(payload.Count + 2)));
		app1.AddRange(payload);

		var result = new List<byte>(jpeg.Length + app1.Count);
		result.AddRange(jpeg.Take(2));
		result.AddRange(app1);
		result.AddRange(jpeg.Skip(2));
		return [.. result];

		static byte[] U16(ushort v) => BitConverter.GetBytes(v);
		static byte[] U32(uint v) => BitConverter.GetBytes(v);
		static byte[] BigEndian(ushort v) => [(byte)(v >> 8), (byte)(v & 0xFF)];

		static byte[] Entry(ushort tag, ushort type, uint count, uint value)
			=> [.. U16(tag), .. U16(type), .. U32(count), .. U32(value)];
	}

	/// <summary>PNG só com cabeçalho válido (IHDR com CRC correto), IDAT mínimo e IEND: a "bomba".</summary>
	internal static byte[] PngHeaderOnly(int width, int height)
	{
		var ihdr = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
		BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
		ihdr[8] = 8; // profundidade
		ihdr[9] = 2; // RGB

		using var stream = new MemoryStream();
		stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
		WriteChunk(stream, "IHDR", ihdr);
		WriteChunk(stream, "IDAT", [0x78, 0x9C, 0x63, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01]);
		WriteChunk(stream, "IEND", []);
		return stream.ToArray();
	}

	/// <summary>
	/// PNG válido de ponta a ponta num tipo de cor específico (0 cinza, 3 paleta, 4 cinza+alfa, 6 RGBA),
	/// montado à mão para exercitar tipos que o encoder do Skia não gera. Os pixels são todos zero.
	/// </summary>
	internal static byte[] PngRaw(int width, int height, byte bitDepth, byte colorType)
	{
		var channels = colorType switch { 0 or 3 => 1, 4 => 2, 6 => 4, _ => throw new ArgumentOutOfRangeException(nameof(colorType)) };
		var bytesPerRow = ((width * channels * bitDepth) + 7) / 8;

		var ihdr = new byte[13];
		BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
		BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
		ihdr[8] = bitDepth;
		ihdr[9] = colorType;

		byte[] compressed;
		using (var buffer = new MemoryStream())
		{
			using (var zlib = new System.IO.Compression.ZLibStream(buffer, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
			{
				zlib.Write(new byte[(bytesPerRow + 1) * height]);
			}

			compressed = buffer.ToArray();
		}

		using var stream = new MemoryStream();
		stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
		WriteChunk(stream, "IHDR", ihdr);

		if (colorType == 3)
		{
			WriteChunk(stream, "PLTE", [0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF]);
		}

		WriteChunk(stream, "IDAT", compressed);
		WriteChunk(stream, "IEND", []);
		return stream.ToArray();
	}

	internal static byte[] Gif1x1() =>
	[
		.. "GIF89a"u8.ToArray(),
		0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00,
		0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00,
		0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
		0x02, 0x02, 0x44, 0x01, 0x00, 0x3B,
	];

	internal static byte[] Bmp1x1()
	{
		var bytes = new byte[58];
		bytes[0] = 0x42; // B
		bytes[1] = 0x4D; // M
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), 58);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), 54);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 1);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), 1);
		BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
		BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 24);
		BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), 4);
		bytes[54] = 0xFF;
		return bytes;
	}

	internal static byte[] Svg(string marker = "x") =>
		Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><title>{marker}</title></svg>");

	/// <summary>Caixa <c>ftyp</c> com marca <c>heic</c>.</summary>
	internal static byte[] HeicHeader() =>
	[
		0x00, 0x00, 0x00, 0x18,
		.. "ftypheic"u8.ToArray(),
		0x00, 0x00, 0x00, 0x00,
		.. "mif1heic"u8.ToArray(),
	];

	private static void WriteChunk(Stream stream, string type, byte[] data)
	{
		var typeBytes = Encoding.ASCII.GetBytes(type);
		var number = new byte[4];
		BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
		stream.Write(number);
		stream.Write(typeBytes);
		stream.Write(data);

		var crc = Crc32([.. typeBytes, .. data]);
		BinaryPrimitives.WriteUInt32BigEndian(number, crc);
		stream.Write(number);
	}

	private static uint Crc32(byte[] bytes)
	{
		var crc = 0xFFFFFFFFu;

		foreach (var b in bytes)
		{
			crc ^= b;

			for (var bit = 0; bit < 8; bit++)
			{
				crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
			}
		}

		return ~crc;
	}
}
