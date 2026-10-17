using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Secco.SDK.Imaging.Internal;
using SkiaSharp;
using Xunit;

namespace Secco.SDK.Imaging.Tests;

public sealed class RenderTests
{
	private static SeccoImageProcessor Processor()
		=> new(
			new SkiaImageCodec(NullLogger<SkiaImageCodec>.Instance),
			Options.Create(new SeccoImagingOptions()),
			NullLogger<SeccoImageProcessor>.Instance);

	private static ImageProcessingRequest Req(int maxSide = 512, CropRectangle? crop = null, ImageOutputFormat format = ImageOutputFormat.WebP)
		=> new([new ImageVariant("v", maxSide)], crop, format);

	private static async Task<ImageOutput> Single(byte[] input, ImageProcessingRequest request)
	{
		var result = await Processor().ProcessAsync(new MemoryStream(input), request);
		result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Code : string.Empty);
		return result.Value.Variants["v"];
	}

	private static SKBitmap Decode(ImageOutput output)
		=> SKBitmap.Decode(output.Content) ?? throw new InvalidOperationException("Saida nao decodifica.");

	private static void ShouldBeNear(SKColor actual, byte red, byte green, byte blue, int tolerance = 10)
	{
		((int)actual.Red).Should().BeCloseTo(red, (uint)tolerance);
		((int)actual.Green).Should().BeCloseTo(green, (uint)tolerance);
		((int)actual.Blue).Should().BeCloseTo(blue, (uint)tolerance);
	}

	[Theory]
	[InlineData("jpeg")]
	[InlineData("png")]
	[InlineData("webp")]
	public async Task Entra_jpeg_png_webp_e_sai_webp(string kind)
	{
		var input = kind switch
		{
			"jpeg" => TestImages.Jpeg(120, 120),
			"png" => TestImages.Png(120, 120),
			_ => TestImages.WebP(120, 120),
		};

		var output = await Single(input, Req());

		output.ContentType.Should().Be("image/webp");
		System.Text.Encoding.ASCII.GetString(output.Content, 0, 4).Should().Be("RIFF");
		System.Text.Encoding.ASCII.GetString(output.Content, 8, 4).Should().Be("WEBP");
	}

	[Fact]
	public async Task Sai_jpeg()
	{
		var output = await Single(TestImages.Png(120, 120), Req(format: ImageOutputFormat.Jpeg));

		output.ContentType.Should().Be("image/jpeg");
		output.Content[0].Should().Be(0xFF);
		output.Content[1].Should().Be(0xD8);
	}

	[Fact]
	public async Task Duas_variantes_uma_chamada()
	{
		var request = new ImageProcessingRequest([new ImageVariant("avatar", 96), new ImageVariant("perfil", 512)]);

		var result = await Processor().ProcessAsync(new MemoryStream(TestImages.Png(1000, 1000)), request);

		result.IsSuccess.Should().BeTrue();
		var avatar = result.Value.Variants["avatar"];
		var perfil = result.Value.Variants["perfil"];
		(avatar.Width, avatar.Height).Should().Be((96, 96));
		(perfil.Width, perfil.Height).Should().Be((512, 512));

		using var decodedAvatar = Decode(avatar);
		using var decodedPerfil = Decode(perfil);
		(decodedAvatar.Width, decodedAvatar.Height).Should().Be((96, 96));
		(decodedPerfil.Width, decodedPerfil.Height).Should().Be((512, 512));
	}

	[Fact]
	public async Task Nao_amplia()
	{
		var output = await Single(TestImages.Png(300, 300), Req(512));

		(output.Width, output.Height).Should().Be((300, 300));
	}

	[Fact]
	public async Task Recorte_por_retangulo()
	{
		var output = await Single(TestImages.Png(200, 100), Req(crop: new CropRectangle(100, 0, 100, 100)));

		(output.Width, output.Height).Should().Be((100, 100));
		using var bitmap = Decode(output);
		ShouldBeNear(bitmap.GetPixel(50, 50), 0, 0, 255);
	}

	[Fact]
	public async Task Quadrado_central()
	{
		var output = await Single(TestImages.Png(300, 100), Req());

		(output.Width, output.Height).Should().Be((100, 100));
	}

	// Armazenada 200x100: TL=R (vermelho), TR=G (verde), BL=B (azul), BR=Y (amarelo).
	// Esperado (TL, TR, BL, BR) exibido, derivado da tabela EXIF e não da implementação.
	[Theory]
	[InlineData(1, "RGBY")] // identidade
	[InlineData(2, "GRYB")] // espelho horizontal
	[InlineData(3, "YBGR")] // girar 180
	[InlineData(4, "BYRG")] // espelho vertical
	[InlineData(5, "RBGY")] // transposta
	[InlineData(6, "BRYG")] // girar 90 horario
	[InlineData(7, "YGBR")] // transversa
	[InlineData(8, "GYRB")] // girar 90 anti-horario
	public async Task Orientacao_aplicada(int orientation, string expectedTlTrBlBr)
	{
		var jpeg = TestImages.JpegWithExif(200, 100, (ushort)orientation, "x", quadrants: true);
		var (displayW, displayH) = orientation >= 5 ? (100, 200) : (200, 100);

		var output = await Single(jpeg, Req(512, new CropRectangle(0, 0, displayW, displayH)));

		(output.Width, output.Height).Should().Be((displayW, displayH));
		using var bitmap = Decode(output);
		(bitmap.Width, bitmap.Height).Should().Be((displayW, displayH));

		var centers = new[]
		{
			(displayW / 4, displayH / 4),
			(displayW * 3 / 4, displayH / 4),
			(displayW / 4, displayH * 3 / 4),
			(displayW * 3 / 4, displayH * 3 / 4),
		};

		for (var i = 0; i < 4; i++)
		{
			var pixel = bitmap.GetPixel(centers[i].Item1, centers[i].Item2);
			Named(pixel).Should().Be(expectedTlTrBlBr[i], $"quadrante {i} da orientação {orientation}");
		}

		static char Named(SKColor c) => (c.Red > 150, c.Green > 150, c.Blue > 150) switch
		{
			(true, false, false) => 'R',
			(false, true, false) => 'G',
			(false, false, true) => 'B',
			(true, true, false) => 'Y',
			_ => '?',
		};
	}

	[Fact]
	public async Task Png_transparente_em_jpeg_fica_branco()
	{
		using var transparent = new SKBitmap(50, 50, SKColorType.Rgba8888, SKAlphaType.Premul);
		transparent.Erase(SKColors.Transparent);
		var png = TestImages.Encode(transparent, SKEncodedImageFormat.Png);

		var output = await Single(png, Req(format: ImageOutputFormat.Jpeg));

		using var bitmap = Decode(output);
		var center = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
		center.Red.Should().BeGreaterThanOrEqualTo(245);
		center.Green.Should().BeGreaterThanOrEqualTo(245);
		center.Blue.Should().BeGreaterThanOrEqualTo(245);
	}

	[Fact]
	public async Task Imagem_alongada_nunca_zera_dimensao()
	{
		var output = await Single(TestImages.Png(4000, 1), Req(96, new CropRectangle(0, 0, 4000, 1)));

		(output.Width, output.Height).Should().Be((96, 1));
	}

	[Theory]
	[InlineData(0, 8)]
	[InlineData(3, 8)]
	[InlineData(4, 16)]
	[InlineData(6, 16)]
	public async Task Png_cinza_paleta_16bits(byte colorType, byte bitDepth)
	{
		var output = await Single(TestImages.PngRaw(64, 64, bitDepth, colorType), Req());

		(output.Width, output.Height).Should().Be((64, 64));
		using var bitmap = Decode(output);
		(bitmap.Width, bitmap.Height).Should().Be((64, 64));
	}

	[Fact]
	public async Task Jpeg_truncado_e_invalido()
	{
		var jpeg = TestImages.Jpeg(400, 400);
		var truncated = jpeg[..(jpeg.Length * 6 / 10)];

		var result = await Processor().ProcessAsync(new MemoryStream(truncated), Req());

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SeccoImagingErrors.InvalidImage);
	}
}
