namespace Secco.SecureGate.Application.Users;

/// <summary>Usuário provisionado (sem segredos).</summary>
/// <param name="Id">Identificador (o <c>sub</c> dos tokens).</param>
/// <param name="Email">E-mail (também o username).</param>
/// <param name="TenantId">Tenant do usuário.</param>
/// <param name="Roles">Perfis do usuário no tenant.</param>
/// <param name="Status">Situação da conta (<see cref="UserStatuses"/>).</param>
public sealed record UserDto(Guid Id, string Email, Guid TenantId, IReadOnlyList<string> Roles, string Status);
