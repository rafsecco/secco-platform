namespace Secco.AdminPortal.Services;

/// <summary>Resumo de um tenant para as telas de gestão (projeção do <c>TenantDto</c> do client).</summary>
/// <param name="Id">Identificador do tenant.</param>
/// <param name="Name">Nome de exibição.</param>
/// <param name="Slug">Identificador curto único.</param>
/// <param name="IsActive">Tenant ativo no catálogo.</param>
/// <param name="CreatedAt">Momento da criação.</param>
public sealed record TenantSummary(Guid Id, string Name, string Slug, bool IsActive, DateTimeOffset CreatedAt);
