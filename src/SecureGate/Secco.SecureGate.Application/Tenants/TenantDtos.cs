using Secco.SecureGate.Domain.Tenants;

namespace Secco.SecureGate.Application.Tenants;

/// <summary>Tenant do catálogo — visão de gestão (NUNCA carrega connection strings, ADR-0020).</summary>
/// <param name="Id">Identificador do tenant.</param>
/// <param name="Name">Nome de exibição.</param>
/// <param name="Slug">Identificador curto único.</param>
/// <param name="IsActive">Tenant ativo no catálogo.</param>
/// <param name="CreatedAt">Momento da criação.</param>
public sealed record TenantDto(Guid Id, string Name, string Slug, bool IsActive, DateTimeOffset CreatedAt)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    /// <param name="tenant">Entidade de origem.</param>
    public static TenantDto FromEntity(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return new TenantDto(tenant.Id, tenant.Name, tenant.Slug, tenant.IsActive, tenant.CreatedAt);
    }
}

/// <summary>
/// Tenant do catálogo com os produtos que têm banco cadastrado — as connection strings
/// ficam de fora por design: a superfície de gestão é write-only para segredos (ADR-0020);
/// só o endpoint de catálogo autorizado por produto as entrega.
/// </summary>
/// <param name="Id">Identificador do tenant.</param>
/// <param name="Name">Nome de exibição.</param>
/// <param name="Slug">Identificador curto único.</param>
/// <param name="IsActive">Tenant ativo no catálogo.</param>
/// <param name="CreatedAt">Momento da criação.</param>
/// <param name="Products">Produtos com banco cadastrado para o tenant.</param>
/// <param name="Federation">Federação de autenticação do tenant (ADR-0026), quando configurada.</param>
public sealed record TenantDetailDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Products,
    TenantFederationDto? Federation);

/// <summary>
/// Federação de autenticação de um tenant (ADR-0026) — visão de gestão. O directory id NÃO é
/// segredo (é o <c>tid</c> esperado do Entra), então aparece aqui, diferente das connection strings.
/// </summary>
/// <param name="Provider">Provedor de federação (<c>entra-id</c> na v1).</param>
/// <param name="DirectoryId">Directory id (tenant do Entra ID da empresa).</param>
/// <param name="IsEnabled">Federação habilitada.</param>
/// <param name="UpdatedAt">Momento da última alteração.</param>
public sealed record TenantFederationDto(string Provider, Guid DirectoryId, bool IsEnabled, DateTimeOffset UpdatedAt)
{
    /// <summary>Projeta a entidade para o DTO.</summary>
    /// <param name="federation">Entidade de origem.</param>
    public static TenantFederationDto FromEntity(TenantFederation federation)
    {
        ArgumentNullException.ThrowIfNull(federation);

        return new TenantFederationDto(
            federation.Provider, federation.DirectoryId, federation.IsEnabled, federation.UpdatedAt);
    }
}
