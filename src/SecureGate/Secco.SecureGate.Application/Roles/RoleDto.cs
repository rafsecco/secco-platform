namespace Secco.SecureGate.Application.Roles;

/// <summary>Role de um tenant com suas permissões (ADR-0021).</summary>
/// <param name="Name">Nome do role (a claim curta <c>role</c> dos tokens).</param>
/// <param name="Permissions">Permissões concedidas (<c>recurso:acao</c>), ordenadas.</param>
public sealed record RoleDto(string Name, IReadOnlyList<string> Permissions);
