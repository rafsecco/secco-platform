using Secco.SharedKernel.Results;

namespace Secco.SDK.Imaging;

/// <summary>
/// Erros de negócio do processamento de imagens. As descrições são fixas: nunca ecoam
/// conteúdo, nome ou metadados do arquivo recebido (ADR-0020).
/// </summary>
public static class SeccoImagingErrors
{
	/// <summary>A entrada excede <see cref="SeccoImagingOptions.MaxInputBytes"/>.</summary>
	public static readonly Error InputTooLarge =
		Error.Validation("Imaging.Image.InputTooLarge", "A imagem excede o tamanho máximo permitido.");

	/// <summary>O formato não é JPEG, PNG ou WebP.</summary>
	public static readonly Error UnsupportedFormat =
		Error.Validation("Imaging.Image.UnsupportedFormat", "Formato de imagem não suportado. Use JPEG, PNG ou WebP.");

	/// <summary>O conteúdo não é uma imagem decodificável.</summary>
	public static readonly Error InvalidImage =
		Error.Validation("Imaging.Image.InvalidImage", "O arquivo não é uma imagem válida.");

	/// <summary>As dimensões excedem <see cref="SeccoImagingOptions.MaxPixels"/>.</summary>
	public static readonly Error TooManyPixels =
		Error.Validation("Imaging.Image.TooManyPixels", "A imagem tem dimensões acima do máximo permitido.");

	/// <summary>O retângulo de recorte cai fora da imagem.</summary>
	public static readonly Error InvalidCrop =
		Error.Validation("Imaging.Image.InvalidCrop", "O recorte está fora dos limites da imagem.");
}
