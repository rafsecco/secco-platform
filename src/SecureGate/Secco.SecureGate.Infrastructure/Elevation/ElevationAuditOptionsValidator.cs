using Microsoft.Extensions.Options;

namespace Secco.SecureGate.Infrastructure.Elevation;

/// <summary>
/// Valida a identidade de auditoria no startup (<c>ValidateOnStart</c>): seção ausente passa
/// (elevação desligada), seção pela metade derruba o startup.
/// </summary>
internal sealed class ElevationAuditOptionsValidator : IValidateOptions<ElevationAuditOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, ElevationAuditOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		return options.TryValidate(out var error)
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(error!);
	}
}
