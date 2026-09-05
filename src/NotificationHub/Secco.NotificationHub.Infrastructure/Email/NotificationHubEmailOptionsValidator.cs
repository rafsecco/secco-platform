using Microsoft.Extensions.Options;

namespace Secco.NotificationHub.Infrastructure.Email;

/// <summary>
/// Valida a configuração de e-mail no startup (issue #14).
/// </summary>
/// <remarks>
/// Fail-fast por decisão (ADR-0020): sem isto, um provider configurado pela metade só falharia
/// no primeiro envio — dentro de um job, com notificação já persistida como <c>Pending</c> e
/// retry do Hangfire mascarando a causa real.
/// </remarks>
internal sealed class NotificationHubEmailOptionsValidator : IValidateOptions<NotificationHubEmailOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, NotificationHubEmailOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		return options.TryValidate(out var error)
			? ValidateOptionsResult.Success
			: ValidateOptionsResult.Fail(error!);
	}
}
