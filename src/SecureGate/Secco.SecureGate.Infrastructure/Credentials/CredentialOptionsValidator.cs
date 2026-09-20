using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Secco.SecureGate.Infrastructure.Credentials;

/// <summary>
/// Fail-fast da configuração de credenciais no startup (ADR-0033/ADR-0020), na mesma postura do
/// <see cref="Cryptography.SecureGateCatalogOptionsValidator"/>: sem base pública válida o
/// SecureGate não sobe fora de Development, porque um link montado do header <c>Host</c> é um
/// sequestro de conta com aparência de e-mail legítimo.
/// </summary>
/// <param name="environment">Ambiente de hospedagem.</param>
internal sealed class CredentialOptionsValidator(IHostEnvironment environment)
	: IValidateOptions<CredentialOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, CredentialOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		return options.TryValidate(environment.IsDevelopment(), out var error)
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(error!);
	}
}
