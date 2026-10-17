using Secco.SharedKernel.Results;

namespace Secco.SDK.Imaging.Internal;

/// <summary>Formatos de entrada aceitos.</summary>
internal enum ImageInputFormat
{
	Jpeg,
	Png,
	WebP,
}

/// <summary>Cabeçalho lido sem decodificar pixels; largura e altura já orientadas.</summary>
internal sealed record ImageHeader(ImageInputFormat Format, int Width, int Height);

/// <summary>Variante renderizada.</summary>
internal sealed record RenderedImage(string Name, byte[] Content, int Width, int Height);

/// <summary>Porta interna para o motor de imagem (ADR-0042).</summary>
internal interface IImageCodec
{
	/// <summary>Lê formato e dimensões orientadas sem decodificar pixels.</summary>
	Result<ImageHeader> Probe(ReadOnlyMemory<byte> data);

	/// <summary>Decodifica, orienta, recorta (coordenadas orientadas), redimensiona e recodifica sem metadados.</summary>
	Result<IReadOnlyList<RenderedImage>> Render(
		ReadOnlyMemory<byte> data,
		CropRectangle crop,
		IReadOnlyList<ImageVariant> variants,
		ImageOutputFormat format,
		int quality,
		CancellationToken cancellationToken);
}
