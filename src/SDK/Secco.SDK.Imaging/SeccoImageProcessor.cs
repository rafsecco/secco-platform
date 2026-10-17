using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Secco.SDK.Imaging.Internal;
using Secco.SharedKernel.Results;

namespace Secco.SDK.Imaging;

internal sealed class SeccoImageProcessor : ISeccoImageProcessor, IDisposable
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

	public Task<Result<ProcessedImage>> ProcessAsync(Stream input, ImageProcessingRequest request, CancellationToken cancellationToken = default)
		=> throw new NotImplementedException(_codec.GetType().Name + _logger.GetType().Name + _slots.CurrentCount);

	public void Dispose() => _slots.Dispose();
}
