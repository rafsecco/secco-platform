using Microsoft.Extensions.Options;
using Secco.SecureGate.Application.Provisioning;

namespace Secco.SecureGate.Infrastructure.Provisioning;

/// <summary>
/// Valida os alvos de provisionamento no startup (<c>ValidateOnStart</c>).
/// </summary>
/// <remarks>
/// A seção inteira ausente é válida — significa "sem automação", e o modo script continua
/// funcionando. O que não é válido é um alvo declarado pela metade: aí a intenção era ligar a
/// automação, e falhar no startup é melhor que descobrir na primeira tentativa de provisionar
/// (ADR-0020, mesma postura de <c>SecureGateClientCredentialsOptions</c>).
/// </remarks>
internal sealed class TenantDatabaseProvisioningOptionsValidator
	: IValidateOptions<TenantDatabaseProvisioningOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, TenantDatabaseProvisioningOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		return options.TryValidate(out var error)
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(error!);
	}
}
