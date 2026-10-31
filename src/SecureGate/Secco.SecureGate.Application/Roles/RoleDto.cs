namespace Secco.SecureGate.Application.Roles;

/// <summary>Role de um tenant com suas permissões (ADR-0021).</summary>
/// <param name="Name">Nome do role (a claim curta <c>role</c> dos tokens).</param>
/// <param name="Permissions">Permissões concedidas (<c>recurso:acao</c>), ordenadas.</param>
/// <param name="IsImplicit">Perfil implícito do tenant (ADR-0056): todo membro ativo o tem, sem atribuição.</param>
public sealed record RoleDto(string Name, IReadOnlyList<string> Permissions, bool IsImplicit = false);
