using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Federation;

/// <summary>Conta DONA do tenant vinculada ao diretório federado (ADR-0026): o <c>oid</c> vem da chave do vínculo.</summary>
/// <param name="UserId">Conta.</param>
/// <param name="ObjectId">Object id da conta no diretório.</param>
/// <param name="DirectoryManaged">A conta é gerida pelo diretório (ADR-0055).</param>
/// <param name="DeactivationOrigin">Quem desativou a conta, quando desativada.</param>
/// <param name="IsDeactivated">A conta está desativada (lockout sem fim); bloqueio temporário não conta.</param>
public sealed record DirectoryLinkedAccount(
	Guid UserId,
	Guid ObjectId,
	bool DirectoryManaged = false,
	DeactivationOrigin DeactivationOrigin = DeactivationOrigin.None,
	bool IsDeactivated = false);

/// <summary>Atribuição de perfil de uma conta, com a origem (ADR-0036).</summary>
/// <param name="UserId">Conta.</param>
/// <param name="RoleId">Perfil do tenant.</param>
/// <param name="RoleName">Nome do perfil.</param>
/// <param name="Origin">Origem da atribuição.</param>
/// <param name="SourceGroupId">Grupo que originou, quando a origem é <c>Directory</c>.</param>
public sealed record DirectoryAssignment(
	Guid UserId, Guid RoleId, string RoleName, RoleAssignmentOrigin Origin, Guid? SourceGroupId);

/// <summary>
/// Porta de persistência da reconciliação de perfis (ADR-0055): contas vinculadas ao diretório e atribuições com
/// origem. Todo método roda no escopo do tenant que o chamador já entrou (ADR-0041) — o <c>tenantId</c> é
/// repetido no argumento para a implementação conferir, nunca para abrir escopo.
/// </summary>
public interface IDirectoryRoleStore
{
	/// <summary>Contas donas do tenant vinculadas ao diretório (provedor <c>EntraId</c>, chave <c>{tid}:{oid}</c>).</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="directoryId">Directory id da federação do tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<DirectoryLinkedAccount>> ListLinkedAccountsAsync(
		Guid tenantId, Guid directoryId, CancellationToken cancellationToken = default);

	/// <summary>Atribuições das contas informadas, só em perfis do tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userIds">Contas.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<DirectoryAssignment>> ListAssignmentsAsync(
		Guid tenantId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken = default);

	/// <summary>Quantas atribuições de origem <c>Directory</c> o tenant tem — o denominador do freio de massa.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<int> CountDirectoryAssignmentsAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Acrescenta a atribuição com origem <c>Directory</c>. Não sobrescreve atribuição existente, de nenhuma origem.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userId">Conta dona do tenant.</param>
	/// <param name="roleId">Perfil do tenant.</param>
	/// <param name="sourceGroupId">Grupo que a justifica.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se a atribuição já existia ou o perfil/conta não pertencem ao tenant.</returns>
	Task<bool> AddDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, Guid sourceGroupId, CancellationToken cancellationToken = default);

	/// <summary>Retira a atribuição, SÓ se a origem for <c>Directory</c> — a manual nunca é tocada.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userId">Conta.</param>
	/// <param name="roleId">Perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se não havia atribuição de origem <c>Directory</c>.</returns>
	Task<bool> RemoveDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, CancellationToken cancellationToken = default);

	/// <summary>Troca o grupo de origem de uma atribuição <c>Directory</c> (outro grupo mapeado passou a justificá-la).</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userId">Conta.</param>
	/// <param name="roleId">Perfil.</param>
	/// <param name="sourceGroupId">Novo grupo de origem.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se não havia atribuição de origem <c>Directory</c>.</returns>
	Task<bool> RetargetDirectoryAssignmentAsync(
		Guid tenantId, Guid userId, Guid roleId, Guid sourceGroupId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Indica se retirar a conta do perfil deixaria o perfil sem NENHUM membro ativo (conta não bloqueada nem
	/// desativada, dona ou com vínculo ativo no tenant) — a guarda do último membro, generalizada (ADR-0036).
	/// Conta já inativa não conta como membro ativo: tirá-la não muda nada.
	/// </summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="roleId">Perfil.</param>
	/// <param name="userId">Conta a retirar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> IsLastActiveMemberAsync(
		Guid tenantId, Guid roleId, Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Tenant elegível ao ciclo: federação ativa e algo a reconciliar.</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="DirectoryId">Directory id da federação.</param>
public sealed record DirectorySyncTenant(Guid TenantId, Guid DirectoryId);

/// <summary>Quais tenants o ciclo percorre (ADR-0055).</summary>
public interface IDirectorySyncTenantSource
{
	/// <summary>
	/// Tenants ativos, com federação habilitada e ao menos um mapeamento grupo→perfil OU atribuição de origem
	/// <c>Directory</c> a desfazer (mapeamento removido não pode deixar perfil preso). O tenant de plataforma
	/// nunca entra.
	/// </summary>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<DirectorySyncTenant>> ListEligibleAsync(CancellationToken cancellationToken = default);
}
