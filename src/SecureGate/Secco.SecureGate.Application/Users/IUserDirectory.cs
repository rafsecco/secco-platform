using Secco.SharedKernel.Results;

namespace Secco.SecureGate.Application.Users;

/// <summary>Dados de criação de um usuário provisionado por administrador (Fase 6.5).</summary>
/// <param name="TenantId">Tenant ao qual o usuário pertence (ADR-0022: o registro carrega o tenant).</param>
/// <param name="Email">E-mail — também o username (único global; o tenant vem do registro no login).</param>
/// <param name="Password">Senha em claro; hasheada pelo Identity na Infrastructure. Nunca logar.</param>
/// <param name="Roles">Roles a atribuir no tenant (ADR-0021); devem existir.</param>
public sealed record CreateUserData(Guid TenantId, string Email, string Password, IReadOnlyList<string> Roles);

/// <summary>Conta com lockout cru e vínculos — a Application deriva situação e permissões.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="TenantId">Tenant.</param>
/// <param name="LockoutEnabled">Lockout habilitado.</param>
/// <param name="LockoutEnd">Fim do bloqueio.</param>
/// <param name="Roles">Perfis, por nome.</param>
/// <param name="ExternalLogins">Nomes dos provedores externos vinculados.</param>
public sealed record UserAccountData(
	Guid Id,
	string Email,
	Guid TenantId,
	bool LockoutEnabled,
	DateTimeOffset? LockoutEnd,
	IReadOnlyList<string> Roles,
	IReadOnlyList<string> ExternalLogins);

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

	/// <summary>Lista os usuários de um tenant com seus roles (sem segredos).</summary>
	/// <param name="tenantId">Tenant dono dos usuários.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<IReadOnlyList<UserDto>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Indica se o usuário existe E pertence ao tenant, numa consulta pontual. Existe para quem só
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

	/// <summary>Conta do tenant; <c>null</c> se não existe ou é de outro tenant.</summary>
	/// <param name="tenantId">Tenant esperado.</param>
	/// <param name="userId">Usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<UserAccountData?> GetAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
