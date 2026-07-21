namespace Secco.SecureGate.Api.Requests;

/// <summary>Payload de criação de tenant.</summary>
/// <param name="Name">Nome de exibição. Obrigatório.</param>
/// <param name="Slug">Identificador curto único (kebab-case minúsculo). Obrigatório.</param>
public sealed record CreateTenantRequest(string? Name, string? Slug);

/// <summary>
/// Payload de cadastro/substituição do banco de um tenant em um produto.
/// Write-only por design (ADR-0020): nenhuma resposta da API devolve a connection string.
/// </summary>
/// <param name="ConnectionString">Connection string do banco dedicado. Obrigatória.</param>
public sealed record UpsertTenantDatabaseRequest(string? ConnectionString);

/// <summary>
/// Payload de cadastro/atualização da federação de autenticação de um tenant (ADR-0026).
/// O directory id NÃO é segredo (é o <c>tid</c> esperado do Entra ID) — aparece na leitura.
/// </summary>
/// <param name="DirectoryId">Directory id (tenant GUID do Entra ID da empresa). Obrigatório.</param>
/// <param name="Enabled">Habilita o login federado. Ausente = habilitado.</param>
public sealed record UpsertTenantFederationRequest(Guid? DirectoryId, bool? Enabled);

/// <summary>Payload de criação de role (ADR-0021).</summary>
/// <param name="Name">Nome do role (sem espaços). Obrigatório.</param>
public sealed record CreateRoleRequest(string? Name);

/// <summary>Payload de substituição das permissões de um role (PUT idempotente).</summary>
/// <param name="Permissions">Conjunto completo de permissões <c>recurso:acao</c> desejado.</param>
public sealed record SetRolePermissionsRequest(IReadOnlyList<string?>? Permissions);
