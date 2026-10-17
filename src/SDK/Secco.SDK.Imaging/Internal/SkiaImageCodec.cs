using Secco.SharedKernel.Results;

namespace Secco.SDK.Imaging.Internal;

internal sealed class SkiaImageCodec : IImageCodec
{
	public Result<ImageHeader> Probe(ReadOnlyMemory<byte> data) => throw new NotImplementedException();

	public Result<IReadOnlyList<RenderedImage>> Render(
		ReadOnlyMemory<byte> data,
		CropRectangle crop,
		IReadOnlyList<ImageVariant> variants,
		ImageOutputFormat format,
		int quality,
		CancellationToken cancellationToken) => throw new NotImplementedException();
}
