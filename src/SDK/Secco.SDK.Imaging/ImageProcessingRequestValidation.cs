namespace Secco.SDK.Imaging;

/// <summary>Validação do pedido: defeito do chamador vira exceção, nunca <c>Result</c>.</summary>
internal static class ImageProcessingRequestValidation
{
	internal const int MaxVariantSide = 4096;

	internal static void ThrowIfInvalid(ImageProcessingRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Variants is null || request.Variants.Count == 0)
		{
			throw new ArgumentException("Informe ao menos uma variante.", nameof(request));
		}

		if (request.Quality is < 1 or > 100)
		{
			throw new ArgumentException("A qualidade deve estar entre 1 e 100.", nameof(request));
		}

		var names = new HashSet<string>(StringComparer.Ordinal);

		foreach (var variant in request.Variants)
		{
			if (variant is null || string.IsNullOrWhiteSpace(variant.Name))
			{
				throw new ArgumentException("Toda variante precisa de um nome.", nameof(request));
			}

			if (variant.MaxSide is < 1 or > MaxVariantSide)
			{
				throw new ArgumentException($"O lado máximo da variante deve estar entre 1 e {MaxVariantSide}.", nameof(request));
			}

			if (!names.Add(variant.Name))
			{
				throw new ArgumentException("Os nomes das variantes devem ser únicos.", nameof(request));
			}
		}
	}
}
