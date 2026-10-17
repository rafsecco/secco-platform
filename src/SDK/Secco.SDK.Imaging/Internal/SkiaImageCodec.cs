using Microsoft.Extensions.Logging;
using Secco.SharedKernel.Results;
using SkiaSharp;

namespace Secco.SDK.Imaging.Internal;

internal sealed partial class SkiaImageCodec(ILogger<SkiaImageCodec> logger) : IImageCodec
{
	public Result<ImageHeader> Probe(ReadOnlyMemory<byte> data)
	{
		if (data.IsEmpty)
		{
			return SeccoImagingErrors.InvalidImage;
		}

		try
		{
			using var skData = SKData.CreateCopy(data.Span);
			using var codec = SKCodec.Create(skData);

			if (codec is null)
			{
				return SeccoImagingErrors.InvalidImage;
			}

			var format = codec.EncodedFormat switch
			{
				SKEncodedImageFormat.Jpeg => (ImageInputFormat?)ImageInputFormat.Jpeg,
				SKEncodedImageFormat.Png => ImageInputFormat.Png,
				SKEncodedImageFormat.Webp => ImageInputFormat.WebP,
				_ => null,
			};

			if (format is null)
			{
				return SeccoImagingErrors.UnsupportedFormat;
			}

			var info = codec.Info;
			var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
				or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

			return new ImageHeader(format.Value, swap ? info.Height : info.Width, swap ? info.Width : info.Height);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// Só o tipo: a mensagem de um decoder pode citar conteúdo do arquivo (ADR-0020).
			var exceptionType = exception.GetType();
			LogProbeFailed(logger, exceptionType);
			return SeccoImagingErrors.InvalidImage;
		}
	}

	public Result<IReadOnlyList<RenderedImage>> Render(
		ReadOnlyMemory<byte> data,
		CropRectangle crop,
		IReadOnlyList<ImageVariant> variants,
		ImageOutputFormat format,
		int quality,
		CancellationToken cancellationToken)
	{
		try
		{
			using var skData = SKData.CreateCopy(data.Span);
			using var codec = SKCodec.Create(skData);

			if (codec is null)
			{
				return SeccoImagingErrors.InvalidImage;
			}

			// Decodifica uma vez, em RGBA 8888 premultiplicado e sRGB: o Skia converte o perfil de cor da fonte.
			var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
			using var source = new SKBitmap(info);

			// Só Success vale: imagem truncada (IncompleteInput) é imagem inválida, não "quase pronta".
			if (codec.GetPixels(info, source.GetPixels()) != SKCodecResult.Success)
			{
				return SeccoImagingErrors.InvalidImage;
			}

			cancellationToken.ThrowIfCancellationRequested();

			using var cropped = OrientAndCrop(source, codec.EncodedOrigin, crop);
			var outputs = new List<RenderedImage>(variants.Count);

			foreach (var variant in variants)
			{
				cancellationToken.ThrowIfCancellationRequested();
				outputs.Add(RenderVariant(cropped, variant, format, quality));
			}

			IReadOnlyList<RenderedImage> rendered = outputs;
			return Result<IReadOnlyList<RenderedImage>>.Success(rendered);
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// Só o tipo: a mensagem de um decoder pode citar conteúdo do arquivo (ADR-0020).
			var exceptionType = exception.GetType();
			LogRenderFailed(logger, exceptionType);
			return SeccoImagingErrors.InvalidImage;
		}
	}

	/// <summary>Aplica a orientação EXIF e o recorte (já em coordenadas orientadas) numa única passada de desenho.</summary>
	private static SKBitmap OrientAndCrop(SKBitmap source, SKEncodedOrigin origin, CropRectangle crop)
	{
		// Sem colorspace de propósito: os pixels já são sRGB e assim nenhum encoder embute perfil ICC.
		var target = new SKBitmap(new SKImageInfo(crop.Width, crop.Height, SKColorType.Rgba8888, SKAlphaType.Premul));

		using var canvas = new SKCanvas(target);
		canvas.Clear(SKColors.Transparent);
		canvas.Translate(-crop.X, -crop.Y);
		canvas.Concat(OrientationMatrix(origin, source.Width, source.Height));
		// Vizinho mais próximo: as transformações de orientação são exatas, sem reamostragem.
		using var image = SKImage.FromBitmap(source);
		canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
		return target;
	}

	/// <summary>Matriz que leva um pixel da imagem armazenada para a imagem exibida (tabela EXIF 1 a 8).</summary>
	private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, float width, float height) => origin switch
	{
		SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
		SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
		SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
		SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
		SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
		SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
		SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
		_ => SKMatrix.Identity,
	};

	private static RenderedImage RenderVariant(SKBitmap cropped, ImageVariant variant, ImageOutputFormat format, int quality)
	{
		// Nunca amplia; nunca zera uma dimensão (4000x1 em 96 vira 96x1).
		var scale = Math.Min(1d, (double)variant.MaxSide / Math.Max(cropped.Width, cropped.Height));
		var width = Math.Max(1, (int)Math.Round(cropped.Width * scale));
		var height = Math.Max(1, (int)Math.Round(cropped.Height * scale));

		var alpha = format == ImageOutputFormat.Jpeg ? SKAlphaType.Opaque : SKAlphaType.Premul;
		using var output = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, alpha));

		using (var canvas = new SKCanvas(output))
		{
			// JPEG não tem alfa: compõe sobre branco opaco em vez de deixar o encoder decidir (fundo preto).
			canvas.Clear(format == ImageOutputFormat.Jpeg ? SKColors.White : SKColors.Transparent);

			var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
			using var image = SKImage.FromBitmap(cropped);
			canvas.DrawImage(image, new SKRect(0, 0, width, height), sampling);
		}

		using var pixmap = output.PeekPixels();
		using var stream = new SKDynamicMemoryWStream();

		// Codificação do zero: só pixels saem, nunca EXIF, XMP ou ICC da entrada.
		var encoded = format == ImageOutputFormat.Jpeg
			? pixmap.Encode(stream, new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore))
			: pixmap.Encode(stream, new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality));

		if (!encoded)
		{
			throw new InvalidOperationException("Falha ao codificar a variante.");
		}

		using var result = stream.DetachAsData();
		return new RenderedImage(variant.Name, result.ToArray(), width, height);
	}

	[LoggerMessage(Level = LogLevel.Debug, Message = "Falha ao ler o cabeçalho da imagem ({ExceptionType}).")]
	private static partial void LogProbeFailed(ILogger logger, Type exceptionType);

	[LoggerMessage(Level = LogLevel.Debug, Message = "Falha ao renderizar a imagem ({ExceptionType}).")]
	private static partial void LogRenderFailed(ILogger logger, Type exceptionType);
}
