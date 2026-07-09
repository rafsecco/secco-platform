namespace Secco.SDK.AspNetCore.Tenancy;

/// <summary>Registro de um tenant no catálogo da plataforma (ADR-0005).</summary>
/// <param name="TenantId">Identificador do tenant.</param>
/// <param name="ConnectionString">Connection string do banco dedicado do tenant. Nunca logar.</param>
public sealed record TenantInfo(Guid TenantId, string ConnectionString);
