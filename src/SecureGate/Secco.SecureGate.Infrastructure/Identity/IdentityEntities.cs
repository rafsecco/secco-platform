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
}

/// <summary>Role (perfil) por tenant — a autorização granular resolve permissões a partir dele (ADR-0021).</summary>
public sealed class Role : IdentityRole<Guid>
{
    /// <summary>Tenant dono do role (ADR-0021: o mapeamento role→permissions é por tenant).</summary>
    public Guid TenantId { get; set; }
}

/// <summary>Claim de usuário (tabela <c>tb_user_claims</c>).</summary>
public sealed class UserClaim : IdentityUserClaim<Guid>;

/// <summary>Associação usuário↔role (tabela <c>tb_user_roles</c>, PK composta de FKs → <c>id_pfk_*</c>).</summary>
public sealed class UserRole : IdentityUserRole<Guid>;

/// <summary>Login externo (tabela <c>tb_user_logins</c>).</summary>
public sealed class UserLogin : IdentityUserLogin<Guid>;

/// <summary>Claim de role — onde as permissões <c>recurso:ação</c> da ADR-0021 vivem (tabela <c>tb_role_claims</c>).</summary>
public sealed class RoleClaim : IdentityRoleClaim<Guid>;

/// <summary>Token de usuário (tabela <c>tb_user_tokens</c>).</summary>
public sealed class UserToken : IdentityUserToken<Guid>;
