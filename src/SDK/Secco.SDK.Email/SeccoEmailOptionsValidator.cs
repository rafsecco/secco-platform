using Microsoft.Extensions.Options;

namespace Secco.SDK.Email;

/// <summary>
/// Valida a configuração de e-mail no startup.
/// </summary>
/// <remarks>
/// Fail-fast por decisão (ADR-0020): sem isto, um provider configurado pela metade só falharia
/// no primeiro envio — dentro de um job, com o efeito já em andamento e retry mascarando a
/// causa real.
/// </remarks>
/// <param name="sectionKey">Chave da seção do produto (ex.: <c>SecureGate:Email</c>), só para a mensagem de erro.</param>
public sealed class SeccoEmailOptionsValidator(string sectionKey) : IValidateOptions<SeccoEmailOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, SeccoEmailOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		return options.TryValidate(sectionKey, out var error)
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(error!);
	}
}
