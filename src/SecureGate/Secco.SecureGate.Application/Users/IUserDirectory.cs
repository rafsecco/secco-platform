using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Dados de criação de um usuário provisionado por administrador (Fase 6.5).</summary>
/// <param name="TenantId">Tenant ao qual o usuário pertence (ADR-0022: o registro carrega o tenant).</param>
/// <param name="Email">E-mail — também o username (único global; o tenant vem do registro no login).</param>
/// <param name="LocalLogin">Se a conta aceita senha local (ADR-0033); nasce sempre sem hash de senha.</param>
/// <param name="Roles">Roles a atribuir no tenant (ADR-0021); devem existir.</param>
/// <param name="DisplayName">Nome de exibição, já normalizado e validado (#30). Opcional.</param>
public sealed record CreateUserData(
	Guid TenantId, string Email, bool LocalLogin, IReadOnlyList<string> Roles, string? DisplayName = null);

/// <summary>Conta com lockout cru e vínculos — a Application deriva situação e permissões.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="TenantId">Tenant.</param>
/// <param name="LockoutEnabled">Lockout habilitado.</param>
/// <param name="LockoutEnd">Fim do bloqueio.</param>
/// <param name="Roles">Perfis, por nome.</param>
/// <param name="ExternalLogins">Nomes dos provedores externos vinculados.</param>
/// <param name="HasPassword">Se a conta já tem hash de senha definido (ADR-0033).</param>
/// <param name="LocalLoginEnabled">Se a conta aceita login local (usuário/senha, ADR-0033).</param>
/// <param name="TwoFactorEnabled">Segundo fator ativado.</param>
/// <param name="DisplayName">Nome de exibição, opcional (#30).</param>
/// <param name="RoleAssignments">
/// Os mesmos perfis de <paramref name="Roles"/>, com a origem de cada atribuição (issue #28).
/// Entra NO FIM: o record é posicional e consumido pelo client NSwag — inserir no meio
/// renumeraria os campos existentes.
/// </param>
/// <param name="Membership">
/// Relação da pessoa com o tenant consultado (<see cref="MembershipStates"/>, ADR-0040). <c>TenantId</c>
/// é o tenant consultado, não o dono da conta; para quem não é dono, os campos de credencial vêm vazios.
/// </param>
/// <param name="OwnerTenantId">Tenant dono da conta (<c>User.TenantId</c>).</param>
public sealed record UserAccountData(
	Guid Id,
	string Email,
	Guid TenantId,
	bool LockoutEnabled,
	DateTimeOffset? LockoutEnd,
	IReadOnlyList<string> Roles,
	IReadOnlyList<string> ExternalLogins,
	bool HasPassword,
	bool LocalLoginEnabled,
	bool TwoFactorEnabled,
	string? DisplayName = null,
	IReadOnlyList<RoleAssignmentData>? RoleAssignments = null,
	string? Membership = null,
	Guid? OwnerTenantId = null);

/// <summary>Um perfil do usuário com a origem da atribuição (issue #28, ADR-0036).</summary>
/// <param name="Name">Nome do perfil.</param>
/// <param name="Origin">Origem da atribuição.</param>
/// <param name="SourceGroupId">Id do grupo do Entra ID que originou, quando <see cref="Origin"/> é <c>Directory</c>.</param>
public sealed record RoleAssignmentData(string Name, RoleAssignmentOrigin Origin, Guid? SourceGroupId);

/// <summary>Pessoa localizada por e-mail para a gestão de vínculos (ADR-0040).</summary>
/// <param name="Id">Identificador da pessoa.</param>
/// <param name="Email">E-mail.</param>
/// <param name="DisplayName">Nome de exibição, opcional.</param>
/// <param name="OwnerTenantId">Tenant dono da conta (<c>User.TenantId</c>).</param>
public sealed record PersonData(Guid Id, string Email, string? DisplayName, Guid OwnerTenantId);

/// <summary>Resultado de atribuir ou remover perfil.</summary>
public enum RoleAssignmentOutcome
{
	/// <summary>Estado final atingido (inclusive se já estava assim).</summary>
	Done,

	/// <summary>Usuário não existe neste tenant.</summary>
	UserNotFound,

	/// <summary>Perfil não existe neste tenant.</summary>
	RoleNotFound,

	/// <summary>Remoção sem efeito: o usuário não era membro do perfil.</summary>
	NotAssigned,

	/// <summary>
	/// A atribuição veio da sincronização de grupo (issue #28, ADR-0036) — remoção manual recusada,
	/// porque o próximo ciclo de sincronização a devolveria em silêncio.
	/// </summary>
	BlockedByDirectoryOrigin,

	/// <summary>A pessoa existe, mas não é dona nem membro (ativo ou suspenso) do tenant (ADR-0040).</summary>
	NotMember,
}

/// <summary>Estado da conta relevante para a sessão.</summary>
/// <param name="TenantId">Tenant.</param>
/// <param name="SecurityStamp">SecurityStamp atual.</param>
/// <param name="LockoutEnabled">Lockout habilitado.</param>
/// <param name="LockoutEnd">Fim do bloqueio.</param>
public sealed record UserSessionState(Guid TenantId, string? SecurityStamp, bool LockoutEnabled, DateTimeOffset? LockoutEnd);

/// <summary>
/// Porta de provisionamento de usuários (ADR-0002): o hash de senha, a política e a
/// atribuição de roles são responsabilidade do ASP.NET Identity, que vive na Infrastructure.
/// A Application orquestra a validação de negócio e converte o resultado em <see cref="Result{T}"/>.
/// </summary>
public interface IUserDirectory
{
	/// <summary>Cria o usuário e atribui os roles; mapeia falhas do Identity para <see cref="Error"/>.</summary>
	/// <param name="data">Dados de criação.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result<UserDto>> CreateAsync(CreateUserData data, CancellationToken cancellationToken = default);

	/// <summary>
	/// Lista as pessoas do tenant — donas e membros (ativos ou suspensos, ADR-0040) — com os perfis DO
	/// tenant (sem segredos).
	/// </summary>
	/// <param name="tenantId">Tenant da listagem.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<UserDto>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Indica se o usuário existe E é DONO do tenant (<c>User.TenantId</c>), numa consulta pontual. "Dono",
	/// e não "membro": com a ADR-0040 a pessoa pode ter vínculo com outros tenants, e as operações de
	/// pessoa (convite, senha, 2FA, desativar...) continuam só do tenant dono. Existe para quem só
	/// precisa da resposta sim/não: listar o tenant inteiro para procurar um id carregaria todos os
	/// usuários e os papéis de cada um a cada chamada.
	/// </summary>
	/// <param name="tenantId">Tenant esperado.</param>
	/// <param name="userId">Usuário a conferir.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> BelongsToTenantAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ativa ou desativa um usuário do tenant. Desativar impede novo login e encerra a sessão na
	/// próxima renovação de token; ativar devolve o acesso.
	/// </summary>
	/// <param name="tenantId">Tenant esperado.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="active"><c>true</c> para ativar; <c>false</c> para desativar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se o usuário não existe ou pertence a outro tenant.</returns>
	Task<bool> SetActiveAsync(Guid tenantId, Guid userId, bool active, CancellationToken cancellationToken = default);

	/// <summary>
	/// Conta de dono ou membro do tenant; <c>null</c> se não existe ou não tem vínculo. Para quem não é
	/// dono, os campos de credencial vêm vazios (ADR-0040).
	/// </summary>
	/// <param name="tenantId">Tenant esperado.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<UserAccountData?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Torna o usuário membro do perfil, ambos no tenant. Idempotente. Exige que a pessoa seja dona ou
	/// membro (ativo ou suspenso) do tenant; senão, <see cref="RoleAssignmentOutcome.NotMember"/>.
	/// </summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="roleName">Nome do perfil já validado.</param>
	/// <param name="origin">
	/// Origem da atribuição (issue #28) — <c>Manual</c> por padrão; a sincronização de grupo
	/// (fora desta entrega) é quem passaria <c>Directory</c>.
	/// </param>
	/// <param name="sourceGroupId">Grupo do Entra ID que originou, quando <paramref name="origin"/> é <c>Directory</c>.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<RoleAssignmentOutcome> AddRoleAsync(
		Guid tenantId,
		Guid userId,
		string roleName,
		RoleAssignmentOrigin origin = RoleAssignmentOrigin.Manual,
		Guid? sourceGroupId = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Retira o usuário do perfil, ambos no tenant. Idempotente. Recusa com
	/// <see cref="RoleAssignmentOutcome.BlockedByDirectoryOrigin"/> quando a atribuição veio da
	/// sincronização de grupo (issue #28) — a única forma de contornar é ajustar o mapeamento ou o
	/// diretório, nunca remover à mão.
	/// </summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="roleName">Nome do perfil já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<RoleAssignmentOutcome> RemoveRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Perfis da pessoa NO tenant informado e só nele (<c>role.TenantId == tenantId</c>) — o que vai no
	/// token (ADR-0040). Não passa pelo <c>UserManager.GetRolesAsync</c>, que devolve os perfis de todos os
	/// tenants da conta.
	/// </summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="tenantId">Tenant do login.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<string>> GetRolesInTenantAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>Indica se o usuário do tenant é membro do perfil do MESMO tenant.</summary>
	/// <param name="tenantId">Tenant.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="roleName">Nome do perfil.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<bool> HasRoleAsync(Guid tenantId, Guid userId, string roleName, CancellationToken cancellationToken = default);

	/// <summary>
	/// Conta operadores de instalação ATIVOS (não desativados nem bloqueados), exceto o informado.
	/// </summary>
	/// <param name="excludingUserId">Usuário alvo da operação, fora da contagem.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<int> CountActiveOperatorsAsync(Guid excludingUserId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Remove o vínculo com um provedor externo. Idempotente: sem vínculo, nada acontece e o
	/// resultado é o mesmo (ADR-0034).
	/// </summary>
	/// <param name="tenantId">Tenant da rota.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="provider">Provedor (ex.: <c>EntraId</c>).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se o usuário não existe ou pertence a outro tenant.</returns>
	Task<bool> RemoveExternalLoginAsync(Guid tenantId, Guid userId, string provider, CancellationToken cancellationToken = default);

	/// <summary>Pessoa pelo e-mail (comparação normalizada pelo Identity); <c>null</c> se não existe.</summary>
	/// <param name="email">E-mail já validado.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PersonData?> FindPersonByEmailAsync(string email, CancellationToken cancellationToken = default);

	/// <summary>Tenant dono da conta (<c>User.TenantId</c>); <c>null</c> se a pessoa não existe.</summary>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Guid?> GetOwnerTenantAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>Estado de sessão do usuário, em qualquer tenant; <c>null</c> se não existe.</summary>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<UserSessionState?> GetSessionStateAsync(Guid userId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Define o nome de exibição do usuário do tenant informado (#30). Já validado pelo chamador;
	/// esta porta só persiste. Não revoga sessão nem entra na trilha aqui — não é evento de
	/// credencial, e o handler decide isso.
	/// </summary>
	/// <param name="tenantId">Tenant esperado.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="displayName">Nome já normalizado (ou <c>null</c> para limpar).</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	/// <returns><c>false</c> se o usuário não existe ou pertence a outro tenant.</returns>
	Task<bool> SetDisplayNameAsync(
		Guid tenantId, Guid userId, string? displayName, CancellationToken cancellationToken = default);
}
