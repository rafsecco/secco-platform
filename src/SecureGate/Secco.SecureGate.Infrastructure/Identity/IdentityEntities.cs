using Microsoft.AspNetCore.Identity;

namespace Secco.SecureGate.Infrastructure.Identity;

/// <summary>
/// Entidades do ASP.NET Identity com chaves Guid e <b>nomes curtos</b>: a
/// <c>SeccoNamingConvention</c> deriva nomes de coluna do nome da classe
/// (<c>User</c> → <c>id_pk_user</c>) — os genéricos do Identity gerariam identificadores inválidos.
/// </summary>
public sealed class User : IdentityUser<Guid>
{
	/// <summary>Tenant do usuário (ADR-0022: o registro carrega o tenant — sem descoberta ambígua no login).</summary>
	public Guid TenantId { get; set; }

	/// <summary>
	/// Se a conta aceita login local (usuário/senha, ADR-0033). Contas só-federadas (ADR-0026)
	/// desligam este login sem apagar a senha eventualmente existente. Nasce <c>true</c>.
	/// </summary>
	public bool LocalLoginEnabled { get; set; } = true;

	/// <summary>
	/// Nome de exibição, opcional (#30). Identidade, não dado de produto: quem provisiona pode
	/// informar, e o dono edita a qualquer momento. Sem valor, todo consumidor cai no e-mail —
	/// nada quebra para quem não usa. Aparado e sem caractere de controle antes de chegar aqui
	/// (<c>DisplayNameRules</c>, ADR-0020: log forging por texto livre indo à trilha e ao token).
	/// </summary>
	public string? DisplayName { get; set; }

	/// <summary>
	/// Conta gerida pelo diretório federado (ADR-0055): a que a sincronização criou, ou a pré-existente vinculada
	/// por <c>oid</c> ao diretório que apareceu no grupo de provisionamento (adoção). Só a conta gerida é
	/// desativada e reativada pela sincronização; a conta só local e a administrativa nunca são. Nasce <c>false</c>.
	/// </summary>
	public bool DirectoryManaged { get; set; }

	/// <summary>
	/// Quem desativou a conta (ADR-0055). <c>None</c> enquanto ativa. Gravada junto com o lockout, pela mesma
	/// operação: a sincronização só reativa a conta cuja origem é <c>Directory</c>.
	/// </summary>
	public Application.Users.DeactivationOrigin DeactivationOrigin { get; set; } = Application.Users.DeactivationOrigin.None;
}

/// <summary>Role (perfil) por tenant — a autorização granular resolve permissões a partir dele (ADR-0021).</summary>
public sealed class Role : IdentityRole<Guid>
{
	/// <summary>Tenant dono do role (ADR-0021: o mapeamento role→permissions é por tenant).</summary>
	public Guid TenantId { get; set; }

	/// <summary>
	/// Perfil implícito do tenant (ADR-0056): todo membro ativo o tem sem atribuição. No máximo um por
	/// tenant (índice único filtrado); nunca atribuído, nunca excluído e nunca existe no tenant de plataforma.
	/// </summary>
	public bool IsImplicit { get; set; }
}

/// <summary>Claim de usuário (tabela <c>tb_user_claims</c>).</summary>
public sealed class UserClaim : IdentityUserClaim<Guid>;

/// <summary>Associação usuário↔role (tabela <c>tb_user_roles</c>, PK composta de FKs → <c>id_pfk_*</c>).</summary>
public sealed class UserRole : IdentityUserRole<Guid>
{
	/// <summary>
	/// Origem da atribuição (issue #28, ADR-0036). Nasce <c>Manual</c> — toda atribuição existente
	/// antes desta coluna converge para cá sem precisar de dado novo.
	/// </summary>
	public Application.Users.RoleAssignmentOrigin Origin { get; set; } = Application.Users.RoleAssignmentOrigin.Manual;

	/// <summary>
	/// Id do grupo do Entra ID que originou a atribuição, quando <see cref="Origin"/> é
	/// <c>Directory</c>; <c>null</c> caso contrário.
	/// </summary>
	public Guid? SourceGroupId { get; set; }
}

/// <summary>Login externo (tabela <c>tb_user_logins</c>).</summary>
public sealed class UserLogin : IdentityUserLogin<Guid>;

/// <summary>Claim de role — onde as permissões <c>recurso:ação</c> da ADR-0021 vivem (tabela <c>tb_role_claims</c>).</summary>
public sealed class RoleClaim : IdentityRoleClaim<Guid>;

/// <summary>Token de usuário (tabela <c>tb_user_tokens</c>).</summary>
public sealed class UserToken : IdentityUserToken<Guid>;
