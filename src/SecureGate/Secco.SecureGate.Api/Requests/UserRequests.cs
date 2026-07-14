namespace Secco.SecureGate.Api.Requests;

/// <summary>Payload de criação de usuário (provisionamento por administrador, Fase 6.5).</summary>
/// <param name="Email">E-mail (também o username). Obrigatório.</param>
/// <param name="Password">Senha em claro (hasheada no servidor pelo Identity). Obrigatória; nunca retornada.</param>
/// <param name="Roles">Roles a atribuir no tenant (opcional).</param>
public sealed record CreateUserRequest(string? Email, string? Password, IReadOnlyList<string>? Roles);
