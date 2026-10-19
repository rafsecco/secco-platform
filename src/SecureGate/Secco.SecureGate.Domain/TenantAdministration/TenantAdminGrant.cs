using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.TenantAdministration;

/// <summary>
/// Concessão de admin de tenant (ADR-0044): a AUTORIDADE explícita, registrada na plataforma, para
/// uma pessoa administrar um tenant on-behalf-of ela mesma, por uma aplicação de plataforma. Sem linha
/// aqui, o escopo <c>securegate:tenant-admin</c> não sai no login e nenhuma rota aceita a autoridade.
/// O tenant da linha é o ADMINISTRADO — não o dono da conta, que pode ser outro (a pessoa não precisa
/// ser membro do tenant que administra).
/// </summary>
public sealed class TenantAdminGrant : BaseEntity
{
	/// <summary>Tamanho máximo aceito para o identificador (<c>sub</c>) de quem concedeu.</summary>
	public const int GrantedByMaxLength = 200;

	private TenantAdminGrant()
	{
		// Construtor de rehidratação do EF Core
		GrantedBy = string.Empty;
	}

	/// <summary>Registra a concessão.</summary>
	/// <param name="userId">Pessoa que passa a administrar o tenant. Obrigatória.</param>
	/// <param name="tenantId">Tenant administrado. Obrigatório.</param>
	/// <param name="grantedBy">Sub de quem concedeu. Obrigatório.</param>
	/// <param name="now">Instante da concessão (por parâmetro, para ser testável).</param>
	/// <exception cref="DomainInvariantException">Se algum argumento violar os invariantes.</exception>
	public TenantAdminGrant(Guid userId, Guid tenantId, string grantedBy, DateTimeOffset now)
	{
		if (userId == Guid.Empty)
		{
			throw new DomainInvariantException("Uma concessão de admin de tenant exige a pessoa.");
		}

		if (tenantId == Guid.Empty)
		{
			throw new DomainInvariantException("Uma concessão de admin de tenant exige o tenant administrado.");
		}

		if (string.IsNullOrWhiteSpace(grantedBy))
		{
			throw new DomainInvariantException("Uma concessão de admin de tenant exige quem concedeu.");
		}

		if (grantedBy.Length > GrantedByMaxLength)
		{
			throw new DomainInvariantException(
				$"O identificador de quem concedeu excede o limite de {GrantedByMaxLength} caracteres.");
		}

		UserId = userId;
		TenantId = tenantId;
		GrantedBy = grantedBy;
		CreatedAt = now;
	}

	/// <summary>Pessoa que administra (coluna <c>id_fk_user</c>).</summary>
	public Guid UserId { get; private set; }

	/// <summary>Tenant administrado (coluna <c>id_fk_tenant</c>) — o que o filtro e o RLS isolam.</summary>
	public Guid TenantId { get; private set; }

	/// <summary>Sub de quem concedeu (coluna <c>ds_granted_by</c>).</summary>
	public string GrantedBy { get; private set; }

	/// <summary>Momento da concessão (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }
}
