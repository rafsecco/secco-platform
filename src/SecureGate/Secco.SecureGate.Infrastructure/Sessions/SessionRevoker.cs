using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Secco.SecureGate.Application.Sessions;
using Secco.SecureGate.Infrastructure.Identity;

namespace Secco.SecureGate.Infrastructure.Sessions;

/// <summary>
/// Revogação única (ADR-0032). A ordem é a segurança: o stamp muda antes da revogação no OpenIddict, então
/// uma falha no meio deixa a conta mais fechada — os produtos já recusam a versão antiga.
/// </summary>
internal sealed partial class SessionRevoker(
	UserManager<User> userManager,
	IOpenIddictAuthorizationManager authorizations,
	IOpenIddictTokenManager tokens,
	ILogger<SessionRevoker> logger) : ISessionRevoker
{
	public async Task RevokeAllAsync(Guid userId, SessionRevocationReason reason, CancellationToken cancellationToken = default)
	{
		var user = await userManager.FindByIdAsync(userId.ToString()).ConfigureAwait(false);

		if (user is null)
		{
			return;
		}

		var stamped = await userManager.UpdateSecurityStampAsync(user).ConfigureAwait(false);

		if (!stamped.Succeeded)
		{
			throw new InvalidOperationException(
				"O Identity recusou a troca do security stamp: " + string.Join(", ", stamped.Errors.Select(e => e.Code)));
		}

		var subject = userId.ToString();
		await authorizations.RevokeBySubjectAsync(subject, cancellationToken).ConfigureAwait(false);
		await tokens.RevokeBySubjectAsync(subject, cancellationToken).ConfigureAwait(false);

		LogRevoked(logger, userId, user.TenantId, reason);
	}

	[LoggerMessage(EventId = 3201, Level = LogLevel.Information,
		Message = "Sessões revogadas: usuário {UserId}, tenant {TenantId}, motivo {Reason}.")]
	private static partial void LogRevoked(ILogger logger, Guid userId, Guid tenantId, SessionRevocationReason reason);
}
