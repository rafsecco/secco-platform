using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.SecureGate.Domain.Elevation;

/// <summary>
/// Concessão de elevação (ADR-0031): a AUTORIDADE explícita, registrada na plataforma, para um
/// usuário de tenant de cliente trocar o próprio token pelo token estreito de leitura de log
/// cross-tenant (RFC 8693, papel <c>installation-log-reader</c>, carimbado pelo emissor). Sem
/// linha aqui, não há elevação — é o núcleo do token exchange que consulta
/// <see cref="IsActiveAt"/> antes de emitir o token trocado.
/// </summary>
public sealed class ElevationGrant : BaseEntity
{
	/// <summary>Tamanho máximo aceito para o identificador (<c>sub</c>) de quem concedeu.</summary>
	public const int GrantedByMaxLength = 200;

	private ElevationGrant()
	{
		// Construtor de rehidratação do EF Core
		GrantedBy = string.Empty;
	}

	/// <summary>Registra a concessão de elevação de um usuário.</summary>
	/// <param name="userId">Usuário que pode elevar. Obrigatório.</param>
	/// <param name="tenantId">Tenant do usuário. Obrigatório.</param>
	/// <param name="grantedBy">Sub de quem concedeu. Obrigatório.</param>
	/// <param name="expiresAt">Expiração opcional da concessão; nulo = sem expiração.</param>
	/// <param name="now">
	/// Instante de referência, recebido por parâmetro em vez de lido do relógio: é o que torna
	/// a validação de <paramref name="expiresAt"/> testável sem flakiness.
	/// </param>
	/// <exception cref="DomainInvariantException">Se algum argumento violar os invariantes.</exception>
	public ElevationGrant(Guid userId, Guid tenantId, string grantedBy, DateTimeOffset? expiresAt, DateTimeOffset now)
	{
		if (userId == Guid.Empty)
		{
			throw new DomainInvariantException("Uma concessão de elevação exige o usuário.");
		}

		if (tenantId == Guid.Empty)
		{
			throw new DomainInvariantException("Uma concessão de elevação exige o tenant do usuário.");
		}

		UserId = userId;
		TenantId = tenantId;
		GrantedBy = ValidateGrantedBy(grantedBy);
		ExpiresAt = ValidateExpiresAt(expiresAt, now);
		CreatedAt = now;
	}

	/// <summary>Usuário que pode elevar (coluna <c>id_fk_user</c>, índice único — uma concessão por usuário).</summary>
	public Guid UserId { get; private set; }

	/// <summary>Tenant do usuário (coluna <c>id_fk_tenant</c>) — valida a rota por tenant e a posse.</summary>
	public Guid TenantId { get; private set; }

	/// <summary>Sub de quem concedeu (coluna <c>ds_granted_by</c>).</summary>
	public string GrantedBy { get; private set; }

	/// <summary>Momento da concessão original (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Expiração da concessão (coluna <c>dt_expires_at</c>); nula = sem expiração.</summary>
	public DateTimeOffset? ExpiresAt { get; private set; }

	/// <summary>
	/// Se a concessão está ativa no instante informado. Este é o PONTO DE CONTATO com o núcleo:
	/// o token exchange chama este método para decidir se autoriza a troca. Assinatura estável —
	/// não alterar.
	/// </summary>
	/// <param name="now">Instante de referência.</param>
	public bool IsActiveAt(DateTimeOffset now) => ExpiresAt is null || ExpiresAt > now;

	/// <summary>Renova a concessão (upsert idempotente): substitui expiração e quem concedeu desta vez.</summary>
	/// <param name="expiresAt">Nova expiração opcional; nulo = sem expiração.</param>
	/// <param name="grantedBy">Sub de quem concedeu desta vez. Obrigatório.</param>
	/// <param name="now">Instante de referência (mesmo motivo do construtor).</param>
	/// <exception cref="DomainInvariantException">Se algum argumento violar os invariantes.</exception>
	public void Renew(DateTimeOffset? expiresAt, string grantedBy, DateTimeOffset now)
	{
		GrantedBy = ValidateGrantedBy(grantedBy);
		ExpiresAt = ValidateExpiresAt(expiresAt, now);
	}

	private static string ValidateGrantedBy(string grantedBy)
	{
		if (string.IsNullOrWhiteSpace(grantedBy))
		{
			throw new DomainInvariantException("Uma concessão de elevação exige quem concedeu.");
		}

		return grantedBy.Length > GrantedByMaxLength
			? throw new DomainInvariantException(
				$"O identificador de quem concedeu excede o limite de {GrantedByMaxLength} caracteres.")
			: grantedBy;
	}

	private static DateTimeOffset? ValidateExpiresAt(DateTimeOffset? expiresAt, DateTimeOffset now) =>
		expiresAt is { } value && value <= now
			? throw new DomainInvariantException("A expiração da concessão de elevação deve ser no futuro.")
			: expiresAt;
}
