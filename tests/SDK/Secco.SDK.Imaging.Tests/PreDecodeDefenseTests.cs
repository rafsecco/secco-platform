using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Secco.SDK.Imaging.Internal;
using Secco.SharedKernel.Results;
using Xunit;

namespace Secco.SDK.Imaging.Tests;

public sealed class PreDecodeDefenseTests
{
	private static readonly ImageProcessingRequest Request = new([new ImageVariant("v", 512)]);

	private static SeccoImageProcessor Processor(IImageCodec codec, Action<SeccoImagingOptions>? configure = null)
	{
		var options = new SeccoImagingOptions();
		configure?.Invoke(options);
		return new SeccoImageProcessor(codec, Options.Create(options), NullLogger<SeccoImageProcessor>.Instance);
	}

	private static SeccoImageProcessor RealProcessor(Action<SeccoImagingOptions>? configure = null)
		=> Processor(new SkiaImageCodec(NullLogger<SkiaImageCodec>.Instance), configure);

	private static Result<IReadOnlyList<RenderedImage>> OneRendered()
	{
		IReadOnlyList<RenderedImage> images = [new RenderedImage("v", [1], 1, 1)];
		return Result<IReadOnlyList<RenderedImage>>.Success(images);
	}

	private static IImageCodec FakeCodec(int width, int height)
	{
		var codec = Substitute.For<IImageCodec>();
		Result<ImageHeader> header = new ImageHeader(ImageInputFormat.Png, width, height);
		codec.Probe(Arg.Any<ReadOnlyMemory<byte>>()).Returns(header);
		codec.Render(default, default!, default!, default, default, default).ReturnsForAnyArgs(OneRendered());
		return codec;
	}

	private static Task<Result<ProcessedImage>> Run(SeccoImageProcessor processor, byte[] bytes, ImageProcessingRequest? request = null)
		=> processor.ProcessAsync(new MemoryStream(bytes), request ?? Request);

	private static void ShouldNotRender(IImageCodec codec)
		=> codec.DidNotReceiveWithAnyArgs().Render(default, default!, default!, default, default, default);

	[Fact]
	public async Task Arquivo_acima_do_limite_em_stream_sem_length()
	{
		var processor = RealProcessor(o => o.MaxInputBytes = 1000);
		await using var stream = new NonSeekableStream(new byte[1001]);

		var result = await processor.ProcessAsync(stream, Request);

		result.IsFailure.Should().BeTrue();
		result.Error.Should().Be(SeccoImagingErrors.InputTooLarge);
	}

	[Fact]
	public async Task Arquivo_com_exatamente_o_limite_passa_da_leitura()
	{
		var png = TestImages.Png(10, 10);
		var padded = new byte[png.Length + 500];
		png.CopyTo(padded, 0);
		var codec = FakeCodec(10, 10);
		var processor = Processor(codec, o => o.MaxInputBytes = padded.Length);

		var result = await Run(processor, padded);

		result.IsSuccess.Should().BeTrue();
		codec.Received(1).Probe(Arg.Any<ReadOnlyMemory<byte>>());
	}

	[Fact]
	public async Task Heic_e_formato_nao_suportado()
	{
		var result = await Run(RealProcessor(), TestImages.HeicHeader());

		result.Error.Should().Be(SeccoImagingErrors.UnsupportedFormat);
	}

	[Theory]
	[InlineData("gif")]
	[InlineData("bmp")]
	public async Task Gif_e_bmp_sao_recusados(string kind)
	{
		var bytes = kind == "gif" ? TestImages.Gif1x1() : TestImages.Bmp1x1();

		var result = await Run(RealProcessor(), bytes);

		result.Error.Should().Be(SeccoImagingErrors.UnsupportedFormat);
	}

	[Theory]
	[InlineData("svg")]
	[InlineData("texto")]
	[InlineData("vazio")]
	public async Task Svg_texto_e_zero_bytes_sao_imagem_invalida(string kind)
	{
		var bytes = kind switch
		{
			"svg" => TestImages.Svg(),
			"texto" => "isto nao e uma imagem"u8.ToArray(),
			_ => [],
		};

		var result = await Run(RealProcessor(), bytes);

		result.Error.Should().Be(SeccoImagingErrors.InvalidImage);
	}

	[Fact]
	public async Task Bomba_de_descompressao_e_recusada_pelo_cabecalho()
	{
		var result = await Run(RealProcessor(), TestImages.PngHeaderOnly(50000, 50000));

		result.Error.Should().Be(SeccoImagingErrors.TooManyPixels);
	}

	[Fact]
	public async Task Bomba_nao_chega_ao_render()
	{
		var codec = FakeCodec(50000, 50000);

		var result = await Run(Processor(codec), TestImages.Png(4, 4));

		result.Error.Should().Be(SeccoImagingErrors.TooManyPixels);
		ShouldNotRender(codec);
	}

	[Fact]
	public async Task Pixels_exatamente_no_limite_passam()
	{
		var codec = FakeCodec(10, 10);

		var result = await Run(Processor(codec, o => o.MaxPixels = 100), TestImages.Png(4, 4));

		result.IsSuccess.Should().BeTrue();
		codec.ReceivedWithAnyArgs(1).Render(default, default!, default!, default, default, default);
	}

	[Fact]
	public async Task Um_pixel_a_mais_nao_passa()
	{
		var codec = FakeCodec(101, 1);

		var result = await Run(Processor(codec, o => o.MaxPixels = 100), TestImages.Png(4, 4));

		result.Error.Should().Be(SeccoImagingErrors.TooManyPixels);
		ShouldNotRender(codec);
	}

	[Theory]
	[InlineData(-1, 0, 10, 10)]
	[InlineData(0, 0, 0, 10)]
	[InlineData(95, 0, 10, 10)]
	public async Task Recorte_fora_dos_limites(int x, int y, int width, int height)
	{
		var codec = FakeCodec(100, 100);
		var request = Request with { Crop = new CropRectangle(x, y, width, height) };

		var result = await Run(Processor(codec), TestImages.Png(4, 4), request);

		result.Error.Should().Be(SeccoImagingErrors.InvalidCrop);
		ShouldNotRender(codec);
	}

	[Fact]
	public async Task Recorte_em_coordenadas_orientadas()
	{
		// 200x100 com Orientation 6 vira 100x200: (0,100,100,100) só cabe nas dimensões orientadas.
		var real = new SkiaImageCodec(NullLogger<SkiaImageCodec>.Instance);
		var codec = Substitute.For<IImageCodec>();
		codec.Probe(Arg.Any<ReadOnlyMemory<byte>>()).Returns(call => real.Probe(call.Arg<ReadOnlyMemory<byte>>()));
		codec.Render(default, default!, default!, default, default, default).ReturnsForAnyArgs(OneRendered());

		var result = await Run(
			Processor(codec),
			TestImages.JpegWithExif(200, 100, 6, "x"),
			Request with { Crop = new CropRectangle(0, 100, 100, 100) });

		result.IsSuccess.Should().BeTrue();
		codec.ReceivedWithAnyArgs(1).Render(default, default!, default!, default, default, default);
	}

	[Fact]
	public async Task Sem_recorte_usa_quadrado_central()
	{
		var codec = FakeCodec(300, 100);

		await Run(Processor(codec), TestImages.Png(4, 4));

		codec.Received(1).Render(
			Arg.Any<ReadOnlyMemory<byte>>(),
			Arg.Is<CropRectangle>(c => c == new CropRectangle(100, 0, 100, 100)),
			Arg.Any<IReadOnlyList<ImageVariant>>(),
			Arg.Any<ImageOutputFormat>(),
			Arg.Any<int>(),
			Arg.Any<CancellationToken>());
	}

	[Fact]
	public async Task Erro_nao_ecoa_conteudo()
	{
		const string marker = "SECCO-SECRET-MARKER";
		var markerBytes = Encoding.ASCII.GetBytes(marker);
		var processor = RealProcessor(o => o.MaxInputBytes = 5000);
		byte[][] inputs =
		[
			TestImages.Svg(marker),
			[.. "nao-imagem-"u8.ToArray(), .. markerBytes],
			TestImages.JpegWithExif(10, 10, 1, marker)[..40],
			[.. TestImages.HeicHeader(), .. markerBytes],
			[.. new byte[6000], .. markerBytes],
		];

		foreach (var input in inputs)
		{
			var result = await Run(processor, input);

			result.IsFailure.Should().BeTrue();
			result.Error.Description.Should().NotContain(marker);
			result.Error.Code.Should().NotContain(marker);
		}
	}

	[Fact]
	public async Task Cancelado_antes_lanca()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		var processor = RealProcessor();

		var act = () => processor.ProcessAsync(new MemoryStream(TestImages.Png(4, 4)), Request, cts.Token);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}
}
