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
/// <param name="Membership">Vínculo com o tenant (<see cref="MembershipText"/>): Owner, Member ou Suspended (ADR-0040).</param>
/// <param name="OwnerTenantId">Tenant dono da conta; só difere do tenant da tela para quem é membro.</param>
public sealed record UserSummary(
	Guid Id,
	string Email,
	IReadOnlyList<string> Roles,
	string Status,
	string Membership = MembershipText.Owner,
	Guid? OwnerTenantId = null);

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
/// <param name="TwoFactorEnabled">Segundo fator ativado (entrega D).</param>
/// <param name="Membership">Vínculo com o tenant da tela: Owner, Member ou Suspended (ADR-0040).</param>
/// <param name="OwnerTenantId">Tenant dono da conta.</param>
public sealed record UserDetail(
	Guid Id,
	string Email,
	string Status,
	DateTimeOffset? LockoutEnd,
	IReadOnlyList<string> Roles,
	IReadOnlyList<string> EffectivePermissions,
	IReadOnlyList<string> ExternalLogins,
	bool HasPassword,
	bool LocalLoginEnabled,
	bool TwoFactorEnabled,
	string Membership = MembershipText.Owner,
	Guid? OwnerTenantId = null);

/// <summary>Vínculo da pessoa com o tenant (ADR-0040): valores do contrato e texto para a tela.</summary>
public static class MembershipText
{
	/// <summary>O tenant é o dono da conta.</summary>
	public const string Owner = "Owner";

	/// <summary>Membro com vínculo ativo.</summary>
	public const string Member = "Member";

	/// <summary>Membro com vínculo suspenso.</summary>
	public const string Suspended = "Suspended";

	/// <summary>Traduz o vínculo; valor desconhecido aparece como veio.</summary>
	/// <param name="membership">Vínculo vindo do SecureGate.</param>
	public static string Describe(string membership) => membership switch
	{
		Owner => "Dono",
		Member => "Membro",
		Suspended => "Suspenso",
		_ => membership,
	};
}

/// <summary>Perfil na listagem do tenant.</summary>
/// <param name="Name">Nome.</param>
/// <param name="Permissions">Permissões gravadas.</param>
public sealed record RoleSummary(string Name, IReadOnlyList<string> Permissions);

/// <summary>Perfil detalhado.</summary>
/// <param name="Name">Nome.</param>
/// <param name="Permissions">Permissões efetivas.</param>
/// <param name="IsReserved">Reservado da plataforma.</param>
/// <param name="MemberCount">Quantidade de membros.</param>
/// <param name="UnknownPermissions">Permissões gravadas que nenhum produto declara no catálogo (ADR-0039).</param>
public sealed record RoleDetail(
	string Name,
	IReadOnlyList<string> Permissions,
	bool IsReserved,
	int MemberCount,
	IReadOnlyList<string> UnknownPermissions);

/// <summary>Permissão declarada no catálogo de um produto (ADR-0039).</summary>
/// <param name="Name">Nome <c>recurso:acao</c> ou família <c>prefixo-{param}:acao</c> (ADR-0043).</param>
/// <param name="Description">Descrição para quem administra perfis.</param>
/// <param name="AlsoDeclaredBy">Outros produtos que declaram o mesmo nome (colisão visível, sem bloqueio).</param>
/// <param name="IsFamily"><c>true</c> se <paramref name="Name"/> é uma família (padrão de instância), não um nome exato.</param>
public sealed record PermissionEntry(string Name, string Description, IReadOnlyList<string> AlsoDeclaredBy, bool IsFamily);

/// <summary>Catálogo de permissões de um produto.</summary>
/// <param name="Product">Identificador do produto.</param>
/// <param name="Permissions">Permissões declaradas.</param>
/// <param name="PublishedAt">Momento da última publicação.</param>
public sealed record ProductPermissions(string Product, IReadOnlyList<PermissionEntry> Permissions, DateTimeOffset PublishedAt);

/// <summary>Permissões órfãs removidas de um perfil.</summary>
/// <param name="Role">Perfil.</param>
/// <param name="Removed">Permissões removidas.</param>
public sealed record RoleUnknownPermissions(string Role, IReadOnlyList<string> Removed);

/// <summary>Resumo da limpeza de permissões órfãs de um tenant.</summary>
/// <param name="RolesChanged">Perfis alterados.</param>
/// <param name="PermissionsRemoved">Total de permissões removidas.</param>
/// <param name="Roles">Detalhe por perfil.</param>
public sealed record RemovedUnknownPermissionsSummary(
	int RolesChanged,
	int PermissionsRemoved,
	IReadOnlyList<RoleUnknownPermissions> Roles);

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

/// <summary>Pessoa com concessão de admin de um tenant (ADR-0044).</summary>
/// <param name="UserId">Pessoa.</param>
/// <param name="Email">E-mail da pessoa.</param>
/// <param name="DisplayName">Nome de exibição, se houver.</param>
/// <param name="OwnerTenantId">Tenant dono da conta — pode não ser o administrado.</param>
/// <param name="GrantedBy">Sub de quem concedeu.</param>
/// <param name="GrantedAt">Momento da concessão.</param>
public sealed record TenantAdminSummary(
	Guid UserId,
	string Email,
	string? DisplayName,
	Guid OwnerTenantId,
	string GrantedBy,
	DateTimeOffset GrantedAt);
