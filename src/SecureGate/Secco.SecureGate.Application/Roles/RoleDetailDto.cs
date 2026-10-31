using Secco.SecureGate.Application.Users;

namespace Secco.SecureGate.Application.Roles;

/// <summary>Perfil detalhado para a gestão.</summary>
/// <param name="Name">Nome (a claim curta <c>role</c> dos tokens).</param>
/// <param name="Permissions">Permissões EFETIVAS — as mesmas que os produtos recebem na resolução.</param>
/// <param name="IsReserved">Perfil reservado da plataforma (não editável nem excluível).</param>
/// <param name="MemberCount">Quantidade de usuários membros.</param>
/// <param name="UnknownPermissions">
/// Permissões do perfil que nenhum catálogo de produto declara (órfãs, ADR-0039): não concedem nada
/// que um produto reconheça; ficam visíveis até alguém removê-las. Sempre vazio em perfil reservado.
/// </param>
/// <param name="IsImplicit">
/// Perfil implícito do tenant (ADR-0056): todo membro ativo o tem sem atribuição. Editável, mas não
/// atribuível nem excluível; <paramref name="MemberCount"/> é sempre 0 (não há atribuições).
/// </param>
public sealed record RoleDetailDto(
	string Name,
	IReadOnlyList<string> Permissions,
	bool IsReserved,
	int MemberCount,
	IReadOnlyList<string> UnknownPermissions,
	bool IsImplicit = false);

/// <summary>Membro de um perfil.</summary>
/// <param name="UserId">Usuário.</param>
/// <param name="Email">E-mail.</param>
/// <param name="Status">Situação (<see cref="Users.UserStatuses"/>).</param>
/// <param name="DisplayName">Nome de exibição, opcional (#30).</param>
/// <param name="Origin">
/// Origem da atribuição a ESTE perfil (issue #28) — a tela de administração usa isto para não
/// deixar o admin tentar remover à mão um membro que a sincronização de grupo devolveria.
/// </param>
/// <param name="SourceGroupId">Id do grupo do Entra ID que originou, quando <paramref name="Origin"/> é <c>Directory</c>.</param>
/// <param name="DirectoryManaged">A conta é gerida pelo diretório federado (ADR-0055).</param>
/// <param name="DeactivationOrigin">Quem desativou a conta (<see cref="DeactivationOrigins"/>).</param>
/// <remarks>
/// <paramref name="DisplayName"/>, <paramref name="Origin"/> e <paramref name="SourceGroupId"/>
/// entram NO FIM: o record é posicional e consumido pelo client NSwag — inserir no meio
/// renumeraria os campos existentes.
/// </remarks>
public sealed record RoleMemberDto(
	Guid UserId,
	string Email,
	string Status,
	string? DisplayName = null,
	RoleAssignmentOrigin Origin = RoleAssignmentOrigin.Manual,
	Guid? SourceGroupId = null,
	bool DirectoryManaged = false,
	string? DeactivationOrigin = null);
