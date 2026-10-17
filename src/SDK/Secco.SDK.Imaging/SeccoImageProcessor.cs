using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SDK.Imaging.Internal;
using Secco.SharedKernel.Results;

namespace Secco.SDK.Imaging;

internal sealed partial class SeccoImageProcessor : ISeccoImageProcessor, IDisposable
{
	private readonly IImageCodec _codec;
	private readonly SeccoImagingOptions _options;
	private readonly ILogger<SeccoImageProcessor> _logger;
	private readonly SemaphoreSlim _slots;

	internal SeccoImageProcessor(IImageCodec codec, IOptions<SeccoImagingOptions> options, ILogger<SeccoImageProcessor> logger)
	{
		_codec = codec;
		_options = options.Value;
		_logger = logger;
		_slots = new SemaphoreSlim(_options.MaxConcurrentOperations, _options.MaxConcurrentOperations);
	}

	public async Task<Result<ProcessedImage>> ProcessAsync(Stream input, ImageProcessingRequest request, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		ImageProcessingRequestValidation.ThrowIfInvalid(request);
		cancellationToken.ThrowIfCancellationRequested();

		// 2. Teto de bytes lido de fato, antes de qualquer decodificação.
		var data = await BoundedInputReader.ReadAsync(input, _options.MaxInputBytes, cancellationToken).ConfigureAwait(false);

		if (data is null)
		{
			return Reject(SeccoImagingErrors.InputTooLarge);
		}

		// 3. Contêineres que o motor não decodifica ganham resposta honesta.
		if (IsoBmffSniffer.IsUnsupportedImageContainer(data))
		{
			return Reject(SeccoImagingErrors.UnsupportedFormat);
		}

		// 4. Formato e dimensões só pelo cabeçalho.
		var probe = _codec.Probe(data);

		if (probe.IsFailure)
		{
			return Reject(probe.Error);
		}

		var header = probe.Value;

		// 5. Bomba de descompressão: recusa pelo cabeçalho, antes de alocar um único pixel.
		if ((long)header.Width * header.Height > _options.MaxPixels)
		{
			return Reject(SeccoImagingErrors.TooManyPixels);
		}

		// 6. Recorte, em coordenadas da imagem já orientada.
		var crop = ResolveCrop(request.Crop, header);

		if (crop is null)
		{
			return Reject(SeccoImagingErrors.InvalidCrop);
		}

		// 7-8. Vaga de concorrência: o trabalho de CPU/memória é limitado por processo.
		await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);

		Result<IReadOnlyList<RenderedImage>> rendered;

		try
		{
			rendered = _codec.Render(data, crop, request.Variants, request.Format, request.Quality, cancellationToken);
		}
		finally
		{
			_slots.Release();
		}

		if (rendered.IsFailure)
		{
			return Reject(rendered.Error);
		}

		// 9. Dicionário ordinal; o tipo de mídia vem do formato pedido, não do arquivo recebido.
		var contentType = request.Format == ImageOutputFormat.Jpeg ? "image/jpeg" : "image/webp";
		var outputs = new Dictionary<string, ImageOutput>(StringComparer.Ordinal);

		foreach (var image in rendered.Value)
		{
			outputs[image.Name] = new ImageOutput(image.Content, contentType, image.Width, image.Height);
		}

		return new ProcessedImage(outputs);
	}

	public void Dispose() => _slots.Dispose();

	private static CropRectangle? ResolveCrop(CropRectangle? requested, ImageHeader header)
	{
		if (requested is null)
		{
			var side = Math.Min(header.Width, header.Height);
			return new CropRectangle((header.Width - side) / 2, (header.Height - side) / 2, side, side);
		}

		var valid = requested.Width > 0 && requested.Height > 0 && requested.X >= 0 && requested.Y >= 0
			&& (long)requested.X + requested.Width <= header.Width
			&& (long)requested.Y + requested.Height <= header.Height;

		return valid ? requested : null;
	}

	private Result<ProcessedImage> Reject(Error error)
	{
		// Só o código estável: nunca nome, conteúdo ou metadados do arquivo (ADR-0020).
		LogRejected(_logger, error.Code);
		return error;
	}

	[LoggerMessage(Level = LogLevel.Debug, Message = "Imagem recusada: {ErrorCode}.")]
	private static partial void LogRejected(ILogger logger, string errorCode);
}
