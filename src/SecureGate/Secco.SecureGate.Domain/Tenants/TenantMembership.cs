namespace Secco.SecureGate.Domain.Tenants;

/// <summary>
/// Vínculo de uma pessoa com um tenant (ADR-0040): a conta é uma só por pessoa e ela pode ser
/// membro de vários tenants. O vínculo diz se a pessoa PODE receber token para o tenant; o que
/// ela pode fazer lá continua sendo dos perfis do tenant (ADR-0021).
/// </summary>
/// <remarks>
/// Chave natural <c>(UserId, TenantId)</c> — não herda de <c>BaseEntity</c> porque não há
/// identidade própria além do par. O vínculo do tenant dono da conta (<c>User.TenantId</c>) nasce
/// junto com a conta e nunca se suspende nem se remove — a regra vive na camada de aplicação.
/// </remarks>
public sealed class TenantMembership
{
	private TenantMembership()
	{
		// Construtor de rehidratação do EF Core
	}

	/// <summary>Cria o vínculo, ativo.</summary>
	/// <param name="userId">Pessoa (usuário). Obrigatório.</param>
	/// <param name="tenantId">Tenant. Obrigatório.</param>
	/// <param name="createdAt">Momento da criação.</param>
	/// <exception cref="ArgumentException">Se algum id for vazio.</exception>
	public TenantMembership(Guid userId, Guid tenantId, DateTimeOffset createdAt)
	{
		ArgumentOutOfRangeException.ThrowIfEqual(userId, Guid.Empty);
		ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);

		UserId = userId;
		TenantId = tenantId;
		IsActive = true;
		CreatedAt = createdAt;
	}

	/// <summary>Pessoa (coluna <c>id_pfk_user</c>).</summary>
	public Guid UserId { get; private set; }

	/// <summary>Tenant (coluna <c>id_pfk_tenant</c>).</summary>
	public Guid TenantId { get; private set; }

	/// <summary>Se o vínculo vale para emitir token (coluna <c>fl_active</c>). Nasce <c>true</c>.</summary>
	public bool IsActive { get; private set; }

	/// <summary>Momento da criação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Suspende o vínculo: a pessoa deixa de receber token para o tenant, sem perder o histórico.</summary>
	public void Suspend() => IsActive = false;

	/// <summary>Reativa um vínculo suspenso.</summary>
	public void Reactivate() => IsActive = true;
}
