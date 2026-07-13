namespace Secco.SecureGate.Application.Roles;

/// <summary>
/// Persistência de roles e suas permissões (ADR-0021: padrão Identity — role por tenant,
/// permissões como claims de ação). A camada de aplicação trabalha com DTOs: as entidades
/// do Identity vivem na Infrastructure (ADR-0002).
/// </summary>
public interface IRoleRepository
{
    /// <summary>Verifica se o tenant existe no catálogo.</summary>
    /// <param name="tenantId">Identificador do tenant.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> TenantExistsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Verifica se o role existe no tenant (comparação normalizada).</summary>
    /// <param name="tenantId">Tenant dono do role.</param>
    /// <param name="name">Nome do role.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> RoleExistsAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Cria um role no tenant (sem permissões).</summary>
    /// <param name="tenantId">Tenant dono do role.</param>
    /// <param name="name">Nome do role.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task CreateRoleAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Lista os roles do tenant com suas permissões, ordenados por nome.</summary>
    /// <param name="tenantId">Tenant dono dos roles.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permissões de um role no tenant; lista vazia se o role não concede nada e
    /// <c>null</c> se o role não existe (a gestão distingue; a resolução não).
    /// </summary>
    /// <param name="tenantId">Tenant dono do role.</param>
    /// <param name="name">Nome do role.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<IReadOnlyList<string>?> GetPermissionsAsync(Guid tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Substitui o conjunto de permissões do role (idempotente). <c>false</c> se o role não existe.</summary>
    /// <param name="tenantId">Tenant dono do role.</param>
    /// <param name="name">Nome do role.</param>
    /// <param name="permissions">Conjunto completo de permissões desejado.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task<bool> ReplacePermissionsAsync(
        Guid tenantId,
        string name,
        IReadOnlyCollection<string> permissions,
        CancellationToken cancellationToken = default);
}
