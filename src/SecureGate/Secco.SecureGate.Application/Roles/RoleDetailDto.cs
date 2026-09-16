namespace Secco.SecureGate.Application.Roles;

/// <summary>Perfil detalhado para a gestão.</summary>
/// <param name="Name">Nome (a claim curta <c>role</c> dos tokens).</param>
/// <param name="Permissions">Permissões EFETIVAS — as mesmas que os produtos recebem na resolução.</param>
/// <param name="IsReserved">Perfil reservado da plataforma (não editável nem excluível).</param>
/// <param name="MemberCount">Quantidade de usuários membros.</param>
public sealed record RoleDetailDto(string Name, IReadOnlyList<string> Permissions, bool IsReserved, int MemberCount);

/// <summary>Membro de um perfil.</summary>
/// <param name="UserId">Usuário.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Status">Situação (<see cref="Users.UserStatuses"/>).</param>
public sealed record RoleMemberDto(Guid UserId, string Email, string Status);
