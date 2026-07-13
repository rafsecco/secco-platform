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
