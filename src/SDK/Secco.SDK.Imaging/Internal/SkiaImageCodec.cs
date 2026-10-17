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
		CancellationToken cancellationToken) => throw new NotImplementedException();

	[LoggerMessage(Level = LogLevel.Debug, Message = "Falha ao ler o cabeçalho da imagem ({ExceptionType}).")]
	private static partial void LogProbeFailed(ILogger logger, Type exceptionType);
}
