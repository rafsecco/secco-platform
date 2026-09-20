using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Secco.SecureGate.Infrastructure.Elevation;

/// <summary>
/// Valida a identidade de auditoria no startup (<c>ValidateOnStart</c>): seção ausente passa
/// (elevação desligada), seção pela metade derruba o startup.
/// </summary>
/// <remarks>
/// É também onde sai o aviso de seção renomeada (ADR-0033): a validação é o único ponto que roda
/// no startup já com as opções ligadas e sabendo de onde elas vieram.
/// </remarks>
/// <param name="logger">Log da aplicação.</param>
internal sealed partial class ElevationAuditOptionsValidator(ILogger<ElevationAuditOptionsValidator> logger)
	: IValidateOptions<ElevationAuditOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, ElevationAuditOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (options.UsedLegacySection)
		{
			LogLegacySection(logger, ElevationAuditOptions.LegacySectionKey, ElevationAuditOptions.SectionKey);
		}

		return options.TryValidate(out var error)
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(error!);
	}

	[LoggerMessage(
		EventId = 3302,
		Level = LogLevel.Warning,
		Message = "A seção '{LegacySection}' continua funcionando, mas foi renomeada para '{Section}' — a mesma identidade agora audita também os eventos de credencial (ADR-0033).")]
	private static partial void LogLegacySection(ILogger logger, string legacySection, string section);
}
