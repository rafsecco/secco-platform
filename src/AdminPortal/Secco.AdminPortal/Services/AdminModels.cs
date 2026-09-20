namespace Secco.AdminPortal.Services;

/// <summary>Detalhe de um tenant (cabeçalho da tela de gestão).</summary>
/// <param name="Id">Identificador do tenant.</param>
/// <param name="Name">Nome de exibição.</param>
/// <param name="Slug">Identificador curto único.</param>
/// <param name="IsActive">Tenant ativo no catálogo.</param>
/// <param name="CreatedAt">Momento da criação.</param>
/// <param name="Products">Produtos com banco cadastrado.</param>
public sealed record TenantDetail(
	Guid Id,
	string Name,
	string Slug,
	bool IsActive,
	DateTimeOffset CreatedAt,
	IReadOnlyList<string> Products);

/// <summary>Usuário na listagem do tenant.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Roles">Perfis.</param>
/// <param name="Status">Situação (Active, Deactivated, LockedOut).</param>
public sealed record UserSummary(Guid Id, string Email, IReadOnlyList<string> Roles, string Status);

/// <summary>Usuário detalhado.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Status">Situação.</param>
/// <param name="LockoutEnd">Fim do bloqueio por tentativas.</param>
/// <param name="Roles">Perfis.</param>
/// <param name="EffectivePermissions">Permissões efetivas.</param>
/// <param name="ExternalLogins">Provedores externos vinculados.</param>
/// <param name="HasPassword">Conta já tem senha definida (ADR-0033).</param>
/// <param name="LocalLoginEnabled">Conta aceita login por senha local (ADR-0033).</param>
public sealed record UserDetail(
	Guid Id,
	string Email,
	string Status,
	DateTimeOffset? LockoutEnd,
	IReadOnlyList<string> Roles,
	IReadOnlyList<string> EffectivePermissions,
	IReadOnlyList<string> ExternalLogins,
	bool HasPassword,
	bool LocalLoginEnabled);

/// <summary>Perfil na listagem do tenant.</summary>
/// <param name="Name">Nome.</param>
/// <param name="Permissions">Permissões gravadas.</param>
public sealed record RoleSummary(string Name, IReadOnlyList<string> Permissions);

/// <summary>Perfil detalhado.</summary>
/// <param name="Name">Nome.</param>
/// <param name="Permissions">Permissões efetivas.</param>
/// <param name="IsReserved">Reservado da plataforma.</param>
/// <param name="MemberCount">Quantidade de membros.</param>
public sealed record RoleDetail(string Name, IReadOnlyList<string> Permissions, bool IsReserved, int MemberCount);

/// <summary>Membro de perfil.</summary>
/// <param name="UserId">Usuário.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Status">Situação.</param>
public sealed record RoleMemberSummary(Guid UserId, string Email, string Status);

/// <summary>Página de membros.</summary>
/// <param name="Items">Membros.</param>
/// <param name="Page">Página atual.</param>
/// <param name="TotalPages">Total de páginas.</param>
/// <param name="TotalCount">Total de membros.</param>
public sealed record MemberPage(IReadOnlyList<RoleMemberSummary> Items, int Page, int TotalPages, long TotalCount);

/// <summary>Texto da situação da conta para a tela.</summary>
public static class UserStatusText
{
	/// <summary>Traduz a situação vinda do SecureGate; valor desconhecido aparece como veio.</summary>
	/// <param name="status">Situação.</param>
	public static string Describe(string status) => status switch
	{
		"Active" => "Ativo",
		"Deactivated" => "Desativado",
		"LockedOut" => "Bloqueado",
		_ => status,
	};
}
