using Secco.SecureGate.Application.Tenants;
using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Sessions;

/// <summary>
/// Versão de sessão atual (ADR-0032). Sempre responde: todo caso que não pode ser aceito vira o mesmo
/// estado revogado, para a consulta não servir de oráculo de existência de conta (ADR-0020).
/// </summary>
public sealed class GetSessionVersionHandler(IUserDirectory userDirectory, ITenantRepository tenants)
{
	/// <summary>Executa a consulta.</summary>
	/// <param name="subject">O <c>sub</c> do token.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<SessionVersionDto> HandleAsync(string? subject, CancellationToken cancellationToken = default)
	{
		if (!Guid.TryParse(subject, out var userId))
		{
			return SessionVersionDto.RevokedState;
		}

		var state = await userDirectory.GetSessionStateAsync(userId, cancellationToken).ConfigureAwait(false);

		if (state is null
			|| UserStatuses.From(state.LockoutEnabled, state.LockoutEnd, DateTimeOffset.UtcNow) != UserStatuses.Active)
		{
			return SessionVersionDto.RevokedState;
		}

		var tenant = await tenants.GetByIdAsync(state.TenantId, cancellationToken).ConfigureAwait(false);

		return tenant is { IsActive: true }
			? new SessionVersionDto(SessionVersion.From(state.SecurityStamp), Revoked: false)
			: SessionVersionDto.RevokedState;
	}
}
