using Secco.SecureGate.Application.Users;
using Secco.SharedKernel.Pagination;

namespace Secco.SecureGate.Application.Directory;

/// <summary>
/// Dados crus de uma pessoa do diretório do tenant — só o que o contrato do diretório expõe (ADR-0045),
/// com o lockout cru para a Application derivar a situação.
/// </summary>
/// <param name="Id">Identificador (o <c>sub</c> dos tokens).</param>
/// <param name="Email">E-mail.</param>
/// <param name="DisplayName">Nome de exibição, opcional.</param>
/// <param name="LockoutEnabled">Lockout habilitado.</param>
/// <param name="LockoutEnd">Fim do bloqueio.</param>
/// <param name="IsOwner">Se o tenant consultado é o dono da conta (<c>User.TenantId</c>).</param>
/// <param name="JobTitle">Cargo, opcional.</param>
/// <param name="Department">Departamento, opcional.</param>
/// <param name="BusinessPhone">Telefone comercial, opcional.</param>
/// <param name="ManagerId">Gestor, já filtrado: só vem preenchido se o gestor também está no diretório do tenant consultado.</param>
/// <param name="AttributesOrigin">Quem escreveu os atributos.</param>
public sealed record DirectoryPersonData(
	Guid Id, string Email, string? DisplayName, bool LockoutEnabled, DateTimeOffset? LockoutEnd, bool IsOwner,
	string? JobTitle, string? Department, string? BusinessPhone, Guid? ManagerId, DirectoryAttributesOrigin AttributesOrigin);

/// <summary>
/// Leitura do diretório de pessoas de UM tenant (ADR-0045): donos da conta mais membros com vínculo
/// ATIVO; vínculo suspenso não aparece. O chamador já entrou no escopo do tenant.
/// </summary>
public interface IDirectoryReader
{
	/// <summary>Lista, paginadas e ordenadas por e-mail, as pessoas do tenant.</summary>
	/// <param name="tenantId">Tenant do token.</param>
	/// <param name="page">Página pedida.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<PagedResult<DirectoryPersonData>> ListAsync(Guid tenantId, PageRequest page, CancellationToken cancellationToken = default);

	/// <summary>Localiza uma pessoa do tenant; <c>null</c> se não existe ou está fora do diretório do tenant.</summary>
	/// <param name="tenantId">Tenant do token.</param>
	/// <param name="userId">Pessoa.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<DirectoryPersonData?> FindAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
}
