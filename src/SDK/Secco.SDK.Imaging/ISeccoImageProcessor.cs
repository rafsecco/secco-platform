using Secco.SharedKernel.Results;

namespace Secco.SDK.Imaging;

/// <summary>
/// Processa com segurança uma imagem enviada por usuário (ADR-0042): valida pelo conteúdo,
/// remove metadados, corrige a orientação, recorta, redimensiona em variantes e recodifica.
/// </summary>
public interface ISeccoImageProcessor
{
	/// <summary>Processa a imagem de entrada e devolve uma saída por variante pedida.</summary>
	/// <param name="input">Fluxo com a imagem (JPEG, PNG ou WebP); lido até o limite configurado, sem confiar em <c>Length</c>.</param>
	/// <param name="request">Variantes, recorte opcional e formato de saída.</param>
	/// <param name="cancellationToken">Cancela a leitura, a espera por vaga e a renderização.</param>
	/// <returns>Sucesso com as variantes, ou um erro de <see cref="SeccoImagingErrors"/>.</returns>
	/// <exception cref="ArgumentException">A requisição é inválida (bug do chamador, não do arquivo).</exception>
	/// <exception cref="OperationCanceledException">O token foi cancelado.</exception>
	Task<Result<ProcessedImage>> ProcessAsync(Stream input, ImageProcessingRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Formato de saída das variantes.</summary>
public enum ImageOutputFormat
{
	/// <summary>WebP com perda.</summary>
	WebP,

	/// <summary>JPEG; transparência é composta sobre fundo branco.</summary>
	Jpeg,
}

/// <summary>Pedido de processamento de imagem.</summary>
/// <param name="Variants">Variantes de saída (ao menos uma, nomes únicos).</param>
/// <param name="Crop">Recorte em coordenadas da imagem já orientada; sem ele, usa-se o quadrado central.</param>
/// <param name="Format">Formato de saída.</param>
/// <param name="Quality">Qualidade de codificação, de 1 a 100.</param>
public sealed record ImageProcessingRequest(
	IReadOnlyList<ImageVariant> Variants,
	CropRectangle? Crop = null,
	ImageOutputFormat Format = ImageOutputFormat.WebP,
	int Quality = 80);

/// <summary>Variante nomeada de saída.</summary>
/// <param name="Name">Nome único dentro do pedido (chave do resultado).</param>
/// <param name="MaxSide">Maior lado em pixels, de 1 a 4096; a imagem nunca é ampliada.</param>
public sealed record ImageVariant(string Name, int MaxSide);

/// <summary>Retângulo de recorte em coordenadas da imagem orientada.</summary>
/// <param name="X">Coluna do canto superior esquerdo.</param>
/// <param name="Y">Linha do canto superior esquerdo.</param>
/// <param name="Width">Largura em pixels.</param>
/// <param name="Height">Altura em pixels.</param>
public sealed record CropRectangle(int X, int Y, int Width, int Height);

/// <summary>Resultado do processamento.</summary>
/// <param name="Variants">Saídas indexadas pelo nome da variante (comparação ordinal).</param>
public sealed record ProcessedImage(IReadOnlyDictionary<string, ImageOutput> Variants);

/// <summary>Uma variante processada.</summary>
/// <param name="Content">Bytes codificados, sem metadados.</param>
/// <param name="ContentType">Tipo de mídia (<c>image/webp</c> ou <c>image/jpeg</c>).</param>
/// <param name="Width">Largura final em pixels.</param>
/// <param name="Height">Altura final em pixels.</param>
public sealed record ImageOutput(byte[] Content, string ContentType, int Width, int Height);
