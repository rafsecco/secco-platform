namespace Secco.SecureGate.Application.Users;

/// <summary>
/// Origem de uma atribuição de perfil a um usuário (issue #28, ADR-0036). Existe para a tela de
/// administração não deixar o admin tentar remover à mão algo que a sincronização de grupo
/// devolveria no próximo ciclo — e para <c>RemoveUserRole</c> recusar essa remoção com um erro
/// explícito em vez de aceitar e desfazer silenciosamente.
/// </summary>
public enum RoleAssignmentOrigin
{
	/// <summary>Atribuído por um admin, pela API/tela de gestão.</summary>
	Manual,

	/// <summary>
	/// Atribuído pela sincronização de grupo do diretório federado — a fonte da verdade enquanto o
	/// mapeamento existir é o grupo, não a atribuição em si.
	/// </summary>
	Directory,
}
