using Microsoft.Extensions.Options;

namespace Secco.SDK.Imaging;

/// <summary>Valida os limites de imagem no startup (fail-fast, ADR-0020).</summary>
/// <param name="sectionKey">Chave da seção do produto, só para a mensagem de erro.</param>
public sealed class SeccoImagingOptionsValidator(string sectionKey) : IValidateOptions<SeccoImagingOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, SeccoImagingOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		var failures = new List<string>();

		if (options.MaxInputBytes < 1)
		{
			failures.Add($"{sectionKey}:MaxInputBytes deve ser maior que zero.");
		}

		if (options.MaxPixels < 1)
		{
			failures.Add($"{sectionKey}:MaxPixels deve ser maior que zero.");
		}

		if (options.MaxConcurrentOperations < 1)
		{
			failures.Add($"{sectionKey}:MaxConcurrentOperations deve ser maior que zero.");
		}

		return failures.Count == 0
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(failures);
	}
}
