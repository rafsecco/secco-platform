using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.TenantAdministration;

/// <summary>
/// Concessão "pode criar tenant" (ADR-0047): autoridade de instalação, dada só pelo operador, para uma
/// pessoa criar tenants on-behalf-of ela mesma. Dado de plataforma, sem tenant — uma por pessoa. O tenant
/// criado nasce administrado por ela (<see cref="TenantAdminGrant.CreatedTenant"/>), até o limite configurado.
/// </summary>
public sealed class TenantCreatorGrant : BaseEntity
{
	private TenantCreatorGrant()
	{
		// Construtor de rehidratação do EF Core
		GrantedBy = string.Empty;
	}

	/// <summary>Registra a concessão.</summary>
	/// <param name="userId">Pessoa que passa a poder criar tenants. Obrigatória.</param>
	/// <param name="grantedBy">Sub de quem concedeu. Obrigatório.</param>
	/// <param name="now">Instante da concessão (por parâmetro, para ser testável).</param>
	/// <exception cref="DomainInvariantException">Se algum argumento violar os invariantes.</exception>
	public TenantCreatorGrant(Guid userId, string grantedBy, DateTimeOffset now)
	{
		if (userId == Guid.Empty)
		{
			throw new DomainInvariantException("Uma concessão de criação de tenant exige a pessoa.");
		}

		if (string.IsNullOrWhiteSpace(grantedBy) || grantedBy.Length > TenantAdminGrant.GrantedByMaxLength)
		{
			throw new DomainInvariantException(
				$"Uma concessão de criação de tenant exige quem concedeu, com até {TenantAdminGrant.GrantedByMaxLength} caracteres.");
		}

		UserId = userId;
		GrantedBy = grantedBy;
		CreatedAt = now;
	}

	/// <summary>Pessoa (coluna <c>id_fk_user</c>, única).</summary>
	public Guid UserId { get; private set; }

	/// <summary>Sub de quem concedeu (coluna <c>ds_granted_by</c>).</summary>
	public string GrantedBy { get; private set; }

	/// <summary>Momento da concessão (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }
}
