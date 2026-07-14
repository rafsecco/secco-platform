namespace Secco.SecureGate.Application.Users;

/// <summary>Usuário da plataforma — visão de gestão (nunca carrega senha/hash, ADR-0020).</summary>
/// <param name="Id">Identificador do usuário.</param>
/// <param name="Email">E-mail (também o username).</param>
/// <param name="TenantId">Tenant do usuário.</param>
/// <param name="Roles">Roles atribuídos no tenant (ADR-0021).</param>
public sealed record UserDto(Guid Id, string Email, Guid TenantId, IReadOnlyList<string> Roles);
