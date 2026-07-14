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

/// <summary>Usuário de um tenant (visão de gestão, sem segredos).</summary>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Email">E-mail (também o username).</param>
/// <param name="Roles">Roles atribuídos no tenant.</param>
public sealed record UserSummary(Guid Id, string Email, IReadOnlyList<string> Roles);

/// <summary>Role de um tenant com suas permissões (ADR-0021).</summary>
/// <param name="Name">Nome do role.</param>
/// <param name="Permissions">Permissões <c>recurso:acao</c> concedidas.</param>
public sealed record RoleSummary(string Name, IReadOnlyList<string> Permissions);
